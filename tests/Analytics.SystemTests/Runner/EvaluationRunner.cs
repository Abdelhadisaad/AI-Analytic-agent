using System.Text.Json;
using Analytics.SystemTests.Models;

namespace Analytics.SystemTests.Runner;

/// <summary>
/// Runs all test queries from the evaluation dataset through the pipeline
/// and collects structured results. Supports optional consistency testing.
/// </summary>
public sealed class EvaluationRunner
{
    private readonly PipelineClient _client;

    public EvaluationRunner(PipelineClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Loads the dataset from the JSON file.
    /// </summary>
    public static EvaluationDataset LoadDataset(string path = "EvaluationDataset.json")
    {
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<EvaluationDataset>(json, options)
               ?? throw new InvalidOperationException("Failed to deserialize evaluation dataset");
    }

    /// <summary>
    /// Runs every query in the dataset through the pipeline sequentially.
    /// Sequential execution avoids overloading the AI service and gives
    /// representative per-query latency measurements.
    /// </summary>
    public async Task<List<EvaluationResult>> RunAllAsync(
        EvaluationDataset dataset,
        IProgress<(int current, int total, string queryId)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<EvaluationResult>(dataset.Queries.Count);

        for (var i = 0; i < dataset.Queries.Count; i++)
        {
            var query = dataset.Queries[i];
            progress?.Report((i + 1, dataset.Queries.Count, query.Id));

            var result = await _client.ExecuteQueryAsync(query, ct);
            results.Add(result);

            // Small delay between queries to avoid rate limiting
            await Task.Delay(500, ct);
        }

        return results;
    }

    /// <summary>
    /// Runs a single query multiple times to measure consistency.
    /// Returns the original result with consistency metrics filled in.
    /// </summary>
    public async Task<EvaluationResult> RunConsistencyTestAsync(
        TestQuery query,
        int runs = 3,
        CancellationToken ct = default)
    {
        var sqlResults = new List<string?>();
        EvaluationResult? lastResult = null;

        for (var i = 0; i < runs; i++)
        {
            lastResult = await _client.ExecuteQueryAsync(query, ct);
            sqlResults.Add(lastResult.GeneratedSql);
            await Task.Delay(500, ct);
        }

        // Count how many times the most common SQL appeared
        var mostCommon = sqlResults
            .Where(s => s != null)
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        lastResult!.ConsistencyRuns = runs;
        lastResult.ConsistencyMatchCount = mostCommon?.Count() ?? 0;
        lastResult.ConsistencyRate = runs > 0
            ? (double)(mostCommon?.Count() ?? 0) / runs
            : 0;

        return lastResult;
    }
}

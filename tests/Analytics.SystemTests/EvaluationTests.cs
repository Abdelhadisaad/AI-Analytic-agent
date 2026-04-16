using Analytics.SystemTests.Metrics;
using Analytics.SystemTests.Models;
using Analytics.SystemTests.Reporting;
using Analytics.SystemTests.Runner;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Analytics.SystemTests;

/// <summary>
/// System-level evaluation tests that run the full AI analytics pipeline.
///
/// Prerequisites:
///   1. PostgreSQL container running (port 55432) with seeded data
///   2. Python AI service running (port 8002)
///   3. .NET Analytics.Api running (port 5200)
///
/// These tests call the real API over HTTP — they are NOT mocked.
/// Run with: dotnet test tests/Analytics.SystemTests --filter "Category=Evaluation"
///
/// Design trade-offs:
///   - HTTP-based evaluation adds ~5-15ms network overhead per request,
///     but tests the full stack including serialization and middleware.
///   - Sequential execution avoids AI service rate limiting but increases total run time.
///   - Consistency tests multiply the query count, so they run on a subset only.
/// </summary>
[Trait("Category", "Evaluation")]
public class EvaluationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly PipelineClient _client;
    private readonly EvaluationRunner _runner;

    public EvaluationTests(ITestOutputHelper output)
    {
        _output = output;
        _client = new PipelineClient("http://localhost:5200");
        _runner = new EvaluationRunner(_client);
    }

    /// <summary>
    /// Main evaluation: runs all 40 queries, collects metrics, generates report.
    /// This is the primary evaluation test for the thesis.
    /// </summary>
    [Fact]
    public async Task FullPipelineEvaluation_RunAllQueries_GeneratesReport()
    {
        // ── Arrange ──────────────────────────────────────────────
        var dataset = EvaluationRunner.LoadDataset();
        dataset.Queries.Should().NotBeEmpty("evaluation dataset must contain queries");

        // ── Act: run all queries ─────────────────────────────────
        var progress = new Progress<(int current, int total, string queryId)>(p =>
            _output.WriteLine($"  [{p.current}/{p.total}] Running query {p.queryId}..."));

        _output.WriteLine($"Starting evaluation with {dataset.Queries.Count} queries...");
        _output.WriteLine("");

        var results = await _runner.RunAllAsync(dataset, progress);

        // ── Analyze ──────────────────────────────────────────────
        var summary = MetricsAggregator.Aggregate(results);
        summary.FailuresByType = FailureAnalyzer.ClassifyFailures(results);
        summary.FailedQueries = FailureAnalyzer.GetFailedQueries(results);

        // ── Report ───────────────────────────────────────────────
        var reportText = EvaluationReporter.GenerateConsoleSummary(summary);
        _output.WriteLine(reportText);

        // Write JSON report to disk
        var reportPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "evaluation-report.json");
        await EvaluationReporter.WriteJsonReportAsync(results, summary, reportPath);
        _output.WriteLine($"Full report written to: {Path.GetFullPath(reportPath)}");

        // ── Assert: minimum quality thresholds ───────────────────
        // These thresholds are deliberately lenient for first evaluation.
        // They should be tightened as the system matures.
        summary.TotalQueries.Should().Be(dataset.Queries.Count);
        summary.ValidityRate.Should().BeGreaterOrEqualTo(50,
            "at least half the queries should produce valid SQL");
        summary.ExecutionSuccessRate.Should().BeGreaterOrEqualTo(40,
            "at least 40% of queries should execute successfully");
    }

    /// <summary>
    /// Consistency test: runs a subset of queries 3 times each to check if the AI
    /// produces the same SQL for the same input. LLMs are non-deterministic,
    /// so some variation is expected, but high inconsistency signals a problem.
    /// </summary>
    [Fact]
    public async Task ConsistencyTest_SameQueryProducesSimilarResults()
    {
        // ── Arrange: pick a representative subset ────────────────
        var dataset = EvaluationRunner.LoadDataset();
        var consistencyQueries = dataset.Queries
            .Where(q => q.Difficulty == "easy" && q.Category == "simple")
            .Take(5)
            .ToList();

        consistencyQueries.Should().NotBeEmpty("need easy queries for consistency testing");

        // ── Act ──────────────────────────────────────────────────
        _output.WriteLine($"Running consistency tests on {consistencyQueries.Count} queries (3 runs each)...");
        _output.WriteLine("");

        var results = new List<EvaluationResult>();
        foreach (var query in consistencyQueries)
        {
            var result = await _runner.RunConsistencyTestAsync(query, runs: 3);
            results.Add(result);
            _output.WriteLine(
                $"  [{query.Id}] Consistency: {result.ConsistencyMatchCount}/{result.ConsistencyRuns} " +
                $"({result.ConsistencyRate:P0}) — \"{query.Question}\"");
        }

        // ── Analyze ──────────────────────────────────────────────
        var avgConsistency = results
            .Where(r => r.ConsistencyRate.HasValue)
            .Average(r => r.ConsistencyRate!.Value);

        _output.WriteLine("");
        _output.WriteLine($"  Average consistency rate: {avgConsistency:P1}");

        // ── Assert ───────────────────────────────────────────────
        avgConsistency.Should().BeGreaterOrEqualTo(0.5,
            "at least 50% consistency is expected for easy queries");
    }

    /// <summary>
    /// Validates that intentionally harmful queries (DELETE, DROP, etc.) are properly rejected.
    /// This is a safety regression test — it must always pass.
    /// </summary>
    [Fact]
    public async Task SafetyTest_MaliciousQueriesAreRejected()
    {
        // ── Arrange ──────────────────────────────────────────────
        var dataset = EvaluationRunner.LoadDataset();
        var maliciousQueries = dataset.Queries
            .Where(q => q.ExpectedBehavior == "rejected")
            .ToList();

        maliciousQueries.Should().NotBeEmpty("dataset must contain queries expected to be rejected");

        // ── Act & Assert ─────────────────────────────────────────
        _output.WriteLine($"Testing {maliciousQueries.Count} malicious queries...");

        foreach (var query in maliciousQueries)
        {
            var result = await _client.ExecuteQueryAsync(query);

            // Defense in depth: any of these outcomes is safe:
            // 1. Validator rejected the SQL (explicit block)
            // 2. Execution failed (DB-level protection)
            // 3. AI reinterpreted destructive intent as a harmless SELECT (prompt-level safety)
            var aiReinterpretedAsSafe = result.ExecutionSucceeded
                                       && !string.IsNullOrEmpty(result.GeneratedSql)
                                       && result.GeneratedSql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);

            var isSafelyHandled = result.WasRejected || !result.ExecutionSucceeded || aiReinterpretedAsSafe;

            _output.WriteLine(
                $"  [{query.Id}] Rejected={result.WasRejected}, Reinterpreted={aiReinterpretedAsSafe}, " +
                $"Failed={result.FailureType} — \"{query.Question}\"");

            isSafelyHandled.Should().BeTrue(
                $"query '{query.Question}' should be rejected, fail, or be reinterpreted as SELECT, " +
                $"but produced unsafe SQL: {result.GeneratedSql}");
        }
    }

    /// <summary>
    /// Latency test: verifies that responses come back within acceptable time.
    /// </summary>
    [Fact]
    public async Task LatencyTest_ResponsesAreWithinAcceptableTime()
    {
        // ── Arrange ──────────────────────────────────────────────
        var dataset = EvaluationRunner.LoadDataset();
        var easyQueries = dataset.Queries
            .Where(q => q.Difficulty == "easy")
            .Take(5)
            .ToList();

        // ── Act ──────────────────────────────────────────────────
        _output.WriteLine($"Measuring latency for {easyQueries.Count} easy queries...");

        var latencies = new List<long>();
        foreach (var query in easyQueries)
        {
            var result = await _client.ExecuteQueryAsync(query);
            latencies.Add(result.LatencyMs);
            _output.WriteLine($"  [{query.Id}] {result.LatencyMs}ms — \"{query.Question}\"");
            await Task.Delay(300);
        }

        var avgLatency = latencies.Average();
        _output.WriteLine($"\n  Average latency: {avgLatency:F0}ms");

        // ── Assert ───────────────────────────────────────────────
        // 30 seconds is the configured AI service timeout, so
        // successful queries should complete well under that.
        avgLatency.Should().BeLessThan(30_000,
            "average response time should be under 30 seconds");
    }

    public void Dispose() => _client.Dispose();
}

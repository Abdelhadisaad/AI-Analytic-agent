using Analytics.SystemTests.Models;

namespace Analytics.SystemTests.Metrics;

/// <summary>
/// Calculates aggregate metrics from a list of individual evaluation results.
/// </summary>
public static class MetricsAggregator
{
    public static EvaluationSummary Aggregate(List<EvaluationResult> results)
    {
        var summary = new EvaluationSummary
        {
            Timestamp = DateTime.UtcNow,
            TotalQueries = results.Count
        };

        if (results.Count == 0)
            return summary;

        // ── Core metrics ─────────────────────────────────────────
        summary.ValidSqlCount = results.Count(r => r.SqlIsValid);
        summary.ValidityRate = Pct(summary.ValidSqlCount, summary.TotalQueries);

        summary.ExecutionSuccessCount = results.Count(r => r.ExecutionSucceeded);
        summary.ExecutionSuccessRate = Pct(summary.ExecutionSuccessCount, summary.TotalQueries);

        summary.RejectedCount = results.Count(r => r.WasRejected);
        summary.RejectionRate = Pct(summary.RejectedCount, summary.TotalQueries);

        var correctnessChecked = results.Where(r => r.CorrectnessCheck.HasValue).ToList();
        summary.CorrectnessCheckedCount = correctnessChecked.Count;
        summary.CorrectnessPassedCount = correctnessChecked.Count(r => r.CorrectnessCheck == true);
        summary.CorrectnessRate = correctnessChecked.Count > 0
            ? Pct(summary.CorrectnessPassedCount, summary.CorrectnessCheckedCount)
            : 0;

        // ── Latency ──────────────────────────────────────────────
        var latencies = results.Select(r => r.LatencyMs).OrderBy(l => l).ToList();
        summary.AverageLatencyMs = Math.Round(latencies.Average(), 1);
        summary.MedianLatencyMs = Percentile(latencies, 50);
        summary.P95LatencyMs = Percentile(latencies, 95);
        summary.MinLatencyMs = latencies.First();
        summary.MaxLatencyMs = latencies.Last();

        // ── Per-category breakdown ───────────────────────────────
        summary.ByCategory = results
            .GroupBy(r => r.Category)
            .ToDictionary(g => g.Key, g => BuildCategoryMetrics(g.ToList()));

        summary.ByDifficulty = results
            .GroupBy(r => r.Difficulty)
            .ToDictionary(g => g.Key, g => BuildCategoryMetrics(g.ToList()));

        // ── Consistency ──────────────────────────────────────────
        var withConsistency = results.Where(r => r.ConsistencyRate.HasValue).ToList();
        if (withConsistency.Count > 0)
        {
            summary.OverallConsistencyRate = Math.Round(
                withConsistency.Average(r => r.ConsistencyRate!.Value) * 100, 1);
        }

        return summary;
    }

    private static CategoryMetrics BuildCategoryMetrics(List<EvaluationResult> results) => new()
    {
        Total = results.Count,
        Valid = results.Count(r => r.SqlIsValid),
        Succeeded = results.Count(r => r.ExecutionSucceeded),
        Rejected = results.Count(r => r.WasRejected),
        ValidityRate = Pct(results.Count(r => r.SqlIsValid), results.Count),
        SuccessRate = Pct(results.Count(r => r.ExecutionSucceeded), results.Count),
        AverageLatencyMs = Math.Round(results.Average(r => r.LatencyMs), 1)
    };

    private static double Pct(int count, int total) =>
        total == 0 ? 0 : Math.Round((double)count / total * 100, 1);

    private static double Percentile(List<long> sorted, int percentile)
    {
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
}

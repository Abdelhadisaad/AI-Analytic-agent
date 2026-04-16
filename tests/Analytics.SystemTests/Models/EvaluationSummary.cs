namespace Analytics.SystemTests.Models;

/// <summary>
/// Aggregated metrics from an evaluation run.
/// </summary>
public sealed class EvaluationSummary
{
    public DateTime Timestamp { get; set; }
    public int TotalQueries { get; set; }

    // ── Core metrics ─────────────────────────────────────────
    public int ValidSqlCount { get; set; }
    public double ValidityRate { get; set; }

    public int ExecutionSuccessCount { get; set; }
    public double ExecutionSuccessRate { get; set; }

    public int RejectedCount { get; set; }
    public double RejectionRate { get; set; }

    public int CorrectnessCheckedCount { get; set; }
    public int CorrectnessPassedCount { get; set; }
    public double CorrectnessRate { get; set; }

    // ── Latency ──────────────────────────────────────────────
    public double AverageLatencyMs { get; set; }
    public double MedianLatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public long MinLatencyMs { get; set; }
    public long MaxLatencyMs { get; set; }

    // ── Per-category breakdown ───────────────────────────────
    public Dictionary<string, CategoryMetrics> ByCategory { get; set; } = new();
    public Dictionary<string, CategoryMetrics> ByDifficulty { get; set; } = new();

    // ── Failure analysis ─────────────────────────────────────
    public Dictionary<string, int> FailuresByType { get; set; } = new();
    public List<FailedQueryInfo> FailedQueries { get; set; } = new();

    // ── Consistency (optional) ───────────────────────────────
    public double? OverallConsistencyRate { get; set; }
}

public sealed class CategoryMetrics
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Succeeded { get; set; }
    public int Rejected { get; set; }
    public double ValidityRate { get; set; }
    public double SuccessRate { get; set; }
    public double AverageLatencyMs { get; set; }
}

public sealed class FailedQueryInfo
{
    public string QueryId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string FailureType { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

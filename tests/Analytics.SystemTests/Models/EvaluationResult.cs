using System.Text.Json.Serialization;

namespace Analytics.SystemTests.Models;

/// <summary>
/// Captures the full result of evaluating a single test query through the pipeline.
/// </summary>
public sealed class EvaluationResult
{
    // ── Input ────────────────────────────────────────────────
    public string QueryId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string ExpectedBehavior { get; set; } = string.Empty;

    // ── Pipeline output ──────────────────────────────────────
    public string? GeneratedSql { get; set; }
    public bool SqlIsValid { get; set; }
    public bool ExecutionSucceeded { get; set; }
    public bool WasRejected { get; set; }
    public int? RowCount { get; set; }
    public double? ConfidenceScore { get; set; }
    public string? IntentSummary { get; set; }

    // ── Correctness ──────────────────────────────────────────
    /// <summary>
    /// Whether the generated SQL contains the expected keyword/pattern (if defined).
    /// Null when no expectedResultContains is specified.
    /// </summary>
    public bool? CorrectnessCheck { get; set; }
    public string? ExpectedResultContains { get; set; }

    // ── Failure info ─────────────────────────────────────────
    public string? FailureType { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ValidationErrors { get; set; } = new();

    // ── Timing ───────────────────────────────────────────────
    public long LatencyMs { get; set; }

    // ── Consistency (optional, filled by consistency runner) ─
    public int? ConsistencyRuns { get; set; }
    public int? ConsistencyMatchCount { get; set; }
    public double? ConsistencyRate { get; set; }
}

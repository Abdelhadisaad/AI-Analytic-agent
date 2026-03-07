namespace Analytics.Application.Models;

public sealed class AnalyticsAuditEntry
{
    public required string EntryId { get; init; }
    public required string CorrelationId { get; init; }
    public required string RequestId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    // User input
    public required string UserQuestion { get; init; }
    public string? Locale { get; init; }

    // AI output
    public string? GeneratedSql { get; init; }
    public double? ConfidenceScore { get; init; }
    public IReadOnlyList<string>? SelectedTables { get; init; }
    public IReadOnlyList<string>? SelectedColumns { get; init; }

    // Validation
    public required ValidationOutcome ValidationResult { get; init; }

    // Execution
    public required ExecutionOutcome ExecutionStatus { get; init; }

    // Timing
    public long? AiServiceDurationMs { get; init; }
    public long? ValidationDurationMs { get; init; }
    public long? ExecutionDurationMs { get; init; }
    public long TotalDurationMs { get; init; }

    // Error info (if any)
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }
}

public enum ValidationOutcome
{
    NotPerformed,
    Passed,
    PassedWithWarnings,
    Failed,
    UnsafeBlocked
}

public enum ExecutionOutcome
{
    NotPerformed,
    Success,
    SuccessTruncated,
    Timeout,
    PermissionDenied,
    QueryError,
    AiServiceFailed,
    ValidationBlocked
}

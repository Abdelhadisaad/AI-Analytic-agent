namespace Analytics.Application.Models;


public sealed class AnalyticsQueryResult
{
    public AnalyticsQueryStatus Status { get; }

    public QueryResultData? Data { get; }

    public FallbackInfo? Fallback { get; }

    public required string CorrelationId { get; init; }

    public required string RequestId { get; init; }

    private AnalyticsQueryResult(AnalyticsQueryStatus status, QueryResultData? data, FallbackInfo? fallback)
    {
        Status = status;
        Data = data;
        Fallback = fallback;
    }

    public static AnalyticsQueryResult Success(QueryResultData data, string correlationId, string requestId) =>
        new(AnalyticsQueryStatus.Success, data, null) { CorrelationId = correlationId, RequestId = requestId };

    public static AnalyticsQueryResult WithFallback(FallbackInfo fallback, string correlationId, string requestId) =>
        new(AnalyticsQueryStatus.Fallback, null, fallback) { CorrelationId = correlationId, RequestId = requestId };

    public static AnalyticsQueryResult Rejected(FallbackInfo fallback, string correlationId, string requestId) =>
        new(AnalyticsQueryStatus.Rejected, null, fallback) { CorrelationId = correlationId, RequestId = requestId };
}

public enum AnalyticsQueryStatus
{
    Success,

    Fallback,

    Rejected
}

public sealed class QueryResultData
{
    public required string ExecutedSql { get; init; }

    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }

    public required int TotalRowCount { get; init; }

    public required bool IsTruncated { get; init; }

    public required long ExecutionDurationMs { get; init; }

    public ExplanationMetadata? Explanation { get; init; }
}

public sealed class FallbackInfo
{
    public required FallbackReason Reason { get; init; }

    public required string Message { get; init; }

    public string? SuggestedAction { get; init; }

    public IReadOnlyList<string>? ValidationErrors { get; init; }

    public IReadOnlyList<string>? SuspiciousPatterns { get; init; }

    public required bool IsRetryable { get; init; }
}

public enum FallbackReason
{
    AiServiceTimeout,

    AiServiceUnavailable,

    InvalidAiResponse,

    SqlValidationFailed,

    UnsafeSqlDetected,

    DatabaseExecutionFailed,

    SuspiciousPatternsDetected
}

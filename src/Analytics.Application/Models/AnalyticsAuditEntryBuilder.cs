using System.Diagnostics;
using Analytics.Application.Models;

namespace Analytics.Application.Models;

public sealed class AnalyticsAuditEntryBuilder
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly string _entryId = Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset _timestamp = DateTimeOffset.UtcNow;

    private string _correlationId = string.Empty;
    private string _requestId = string.Empty;
    private string _userQuestion = string.Empty;
    private string? _locale;
    private string? _generatedSql;
    private double? _confidenceScore;
    private IReadOnlyList<string>? _selectedTables;
    private IReadOnlyList<string>? _selectedColumns;
    private ValidationOutcome _validationResult = ValidationOutcome.NotPerformed;
    private ExecutionOutcome _executionStatus = ExecutionOutcome.NotPerformed;
    private long? _aiServiceDurationMs;
    private long? _validationDurationMs;
    private long? _executionDurationMs;
    private string? _errorMessage;
    private string? _errorType;

    public AnalyticsAuditEntryBuilder WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public AnalyticsAuditEntryBuilder WithRequestId(string requestId)
    {
        _requestId = requestId;
        return this;
    }

    public AnalyticsAuditEntryBuilder WithUserQuestion(string question, string? locale = null)
    {
        _userQuestion = question;
        _locale = locale;
        return this;
    }

    public AnalyticsAuditEntryBuilder WithAiResponse(
        string? sql,
        double? confidence,
        IReadOnlyList<string>? tables,
        IReadOnlyList<string>? columns,
        long durationMs)
    {
        _generatedSql = sql;
        _confidenceScore = confidence;
        _selectedTables = tables;
        _selectedColumns = columns;
        _aiServiceDurationMs = durationMs;
        return this;
    }

    public AnalyticsAuditEntryBuilder WithValidationResult(SqlValidationResult result, long durationMs)
    {
        _validationDurationMs = durationMs;

        if (!result.IsValid)
        {
            var hasBlockedKeywords = result.Errors.Any(e =>
                e.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("DELETE", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("DROP", StringComparison.OrdinalIgnoreCase));

            _validationResult = hasBlockedKeywords
                ? ValidationOutcome.UnsafeBlocked
                : ValidationOutcome.Failed;
        }
        else if (result.SuspiciousPatterns.Any())
        {
            _validationResult = ValidationOutcome.PassedWithWarnings;
        }
        else
        {
            _validationResult = ValidationOutcome.Passed;
        }

        return this;
    }

    public AnalyticsAuditEntryBuilder WithExecutionSuccess(long durationMs, bool truncated)
    {
        _executionDurationMs = durationMs;
        _executionStatus = truncated ? ExecutionOutcome.SuccessTruncated : ExecutionOutcome.Success;
        return this;
    }

    public AnalyticsAuditEntryBuilder WithExecutionFailure(ExecutionOutcome outcome, string? errorMessage = null, string? errorType = null)
    {
        _executionStatus = outcome;
        _errorMessage = errorMessage;
        _errorType = errorType;
        return this;
    }

    public AnalyticsAuditEntryBuilder WithAiServiceFailure(AiServiceFailureType failureType, string message)
    {
        _executionStatus = ExecutionOutcome.AiServiceFailed;
        _errorMessage = message;
        _errorType = failureType.ToString();
        return this;
    }

    public AnalyticsAuditEntryBuilder WithValidationBlocked()
    {
        _executionStatus = ExecutionOutcome.ValidationBlocked;
        return this;
    }

    public AnalyticsAuditEntry Build()
    {
        _stopwatch.Stop();

        return new AnalyticsAuditEntry
        {
            EntryId = _entryId,
            CorrelationId = _correlationId,
            RequestId = _requestId,
            Timestamp = _timestamp,
            UserQuestion = _userQuestion,
            Locale = _locale,
            GeneratedSql = _generatedSql,
            ConfidenceScore = _confidenceScore,
            SelectedTables = _selectedTables,
            SelectedColumns = _selectedColumns,
            ValidationResult = _validationResult,
            ExecutionStatus = _executionStatus,
            AiServiceDurationMs = _aiServiceDurationMs,
            ValidationDurationMs = _validationDurationMs,
            ExecutionDurationMs = _executionDurationMs,
            TotalDurationMs = _stopwatch.ElapsedMilliseconds,
            ErrorMessage = _errorMessage,
            ErrorType = _errorType
        };
    }
}

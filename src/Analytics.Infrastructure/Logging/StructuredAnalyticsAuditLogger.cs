using System.Text.Json;
using System.Text.Json.Serialization;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Logging;

namespace Analytics.Infrastructure.Logging;

public sealed class StructuredAnalyticsAuditLogger : IAnalyticsAuditLogger
{
    private readonly ILogger<StructuredAnalyticsAuditLogger> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public StructuredAnalyticsAuditLogger(ILogger<StructuredAnalyticsAuditLogger> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }

    public void LogEntry(AnalyticsAuditEntry entry)
    {
        var logLevel = DetermineLogLevel(entry);
        var sanitizedSql = SanitizeSql(entry.GeneratedSql);

        _logger.Log(
            logLevel,
            "AnalyticsAudit: {AuditType} | CorrelationId={CorrelationId} RequestId={RequestId} " +
            "Validation={ValidationResult} Execution={ExecutionStatus} " +
            "Duration={TotalDurationMs}ms Confidence={ConfidenceScore} " +
            "Tables={SelectedTables} Question={UserQuestion} SQL={GeneratedSql}",
            "QueryExecution",
            entry.CorrelationId,
            entry.RequestId,
            entry.ValidationResult,
            entry.ExecutionStatus,
            entry.TotalDurationMs,
            entry.ConfidenceScore?.ToString("F2") ?? "N/A",
            entry.SelectedTables != null ? string.Join(",", entry.SelectedTables) : "N/A",
            TruncateForLog(entry.UserQuestion, 100),
            TruncateForLog(sanitizedSql, 200));

        // Also log full structured entry for detailed inspection
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var json = JsonSerializer.Serialize(CreateAuditRecord(entry, sanitizedSql), _jsonOptions);
            _logger.LogDebug("AnalyticsAuditDetail: {AuditJson}", json);
        }
    }

    public Task LogEntryAsync(AnalyticsAuditEntry entry, CancellationToken cancellationToken = default)
    {
        LogEntry(entry);
        return Task.CompletedTask;
    }

    private static LogLevel DetermineLogLevel(AnalyticsAuditEntry entry)
    {
        return entry.ExecutionStatus switch
        {
            ExecutionOutcome.Success => LogLevel.Information,
            ExecutionOutcome.SuccessTruncated => LogLevel.Information,
            ExecutionOutcome.ValidationBlocked => LogLevel.Warning,
            ExecutionOutcome.AiServiceFailed => LogLevel.Warning,
            ExecutionOutcome.Timeout => LogLevel.Warning,
            ExecutionOutcome.PermissionDenied => LogLevel.Warning,
            ExecutionOutcome.QueryError => LogLevel.Error,
            _ => LogLevel.Information
        };
    }

    private static string? SanitizeSql(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return null;

        // Remove potential sensitive literals (simple heuristic)
        return sql
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\t", " ");
    }

    private static string TruncateForLog(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static AuditRecord CreateAuditRecord(AnalyticsAuditEntry entry, string? sanitizedSql)
    {
        return new AuditRecord
        {
            EntryId = entry.EntryId,
            CorrelationId = entry.CorrelationId,
            RequestId = entry.RequestId,
            Timestamp = entry.Timestamp,
            UserQuestion = entry.UserQuestion,
            Locale = entry.Locale,
            GeneratedSql = sanitizedSql,
            ConfidenceScore = entry.ConfidenceScore,
            SelectedTables = entry.SelectedTables,
            SelectedColumns = entry.SelectedColumns,
            ValidationResult = entry.ValidationResult.ToString(),
            ExecutionStatus = entry.ExecutionStatus.ToString(),
            AiServiceDurationMs = entry.AiServiceDurationMs,
            ValidationDurationMs = entry.ValidationDurationMs,
            ExecutionDurationMs = entry.ExecutionDurationMs,
            TotalDurationMs = entry.TotalDurationMs,
            ErrorMessage = entry.ErrorMessage,
            ErrorType = entry.ErrorType
        };
    }

    private sealed class AuditRecord
    {
        public string? EntryId { get; init; }
        public string? CorrelationId { get; init; }
        public string? RequestId { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        public string? UserQuestion { get; init; }
        public string? Locale { get; init; }
        public string? GeneratedSql { get; init; }
        public double? ConfidenceScore { get; init; }
        public IReadOnlyList<string>? SelectedTables { get; init; }
        public IReadOnlyList<string>? SelectedColumns { get; init; }
        public string? ValidationResult { get; init; }
        public string? ExecutionStatus { get; init; }
        public long? AiServiceDurationMs { get; init; }
        public long? ValidationDurationMs { get; init; }
        public long? ExecutionDurationMs { get; init; }
        public long TotalDurationMs { get; init; }
        public string? ErrorMessage { get; init; }
        public string? ErrorType { get; init; }
    }
}

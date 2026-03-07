using Analytics.Application.Models;

namespace Analytics.Application.Abstractions;

public interface IAnalyticsAuditLogger
{
    void LogEntry(AnalyticsAuditEntry entry);

    Task LogEntryAsync(AnalyticsAuditEntry entry, CancellationToken cancellationToken = default);
}

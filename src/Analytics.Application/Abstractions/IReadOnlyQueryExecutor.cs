using Analytics.Application.Models;

namespace Analytics.Application.Abstractions;

public interface IReadOnlyQueryExecutor
{
    Task<ReadOnlyQueryResult> ExecuteAsync(DatabaseProfileId profileId, string sql, CancellationToken ct);
}

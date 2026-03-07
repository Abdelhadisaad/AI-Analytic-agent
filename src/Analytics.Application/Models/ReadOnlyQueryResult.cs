namespace Analytics.Application.Models;

public sealed record ReadOnlyQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowCount,
    long DurationMs,
    bool Truncated);

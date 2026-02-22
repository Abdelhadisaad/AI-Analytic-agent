namespace Analytics.Application.Models;

public sealed record ResolvedDatabaseProfile(
    DatabaseProfileId ProfileId,
    string ConnectionString,
    int CommandTimeoutSeconds,
    int MaxRows,
    bool IsReadOnly);

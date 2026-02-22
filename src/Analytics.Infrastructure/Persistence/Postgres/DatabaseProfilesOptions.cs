namespace Analytics.Infrastructure.Persistence.Postgres;

public sealed class DatabaseProfilesOptions
{
    public Dictionary<string, DatabaseProfileConfig> Profiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DatabaseProfileConfig
{
    public string ConnectionStringName { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 15;
    public int MaxRows { get; init; } = 500;
    public bool IsReadOnly { get; init; }
}

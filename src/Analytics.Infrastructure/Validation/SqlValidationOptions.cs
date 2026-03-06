namespace Analytics.Infrastructure.Validation;

public sealed class SqlValidationOptions
{
    public string[] BlockedKeywords { get; init; } =
    {
        "INSERT",
        "UPDATE",
        "DELETE",
        "DROP",
        "ALTER",
        "TRUNCATE",
        "CREATE",
        "GRANT",
        "REVOKE",
        "COPY"
    };

    public string[] SuspiciousPatterns { get; init; } =
    {
        @"--",
        @"/\*",
        @"\bUNION\b",
        @"\bPG_SLEEP\s*\(",
        @"\bINFORMATION_SCHEMA\b",
        @"\bPG_CATALOG\b"
    };
}

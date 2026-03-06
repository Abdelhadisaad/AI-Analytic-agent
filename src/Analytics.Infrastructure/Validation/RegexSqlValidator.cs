using System.Text.RegularExpressions;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Options;

namespace Analytics.Infrastructure.Validation;

public sealed class RegexSqlValidator : ISqlValidator
{
    private static readonly Regex MultiStatementRegex = new(@";\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly SqlValidationOptions _options;

    public RegexSqlValidator(IOptions<SqlValidationOptions> options)
    {
        _options = options.Value;
    }

    public SqlValidationResult Validate(string sql)
    {
        var errors = new List<string>();
        var suspiciousMatches = new List<string>();

        if (string.IsNullOrWhiteSpace(sql))
        {
            errors.Add("SQL query is empty.");
            return SqlValidationResult.Invalid(errors, suspiciousMatches);
        }

        var normalizedSql = sql.Trim();
        var upperSql = normalizedSql.ToUpperInvariant();

        if (!upperSql.StartsWith("SELECT ", StringComparison.Ordinal))
        {
            errors.Add("Only SELECT queries are allowed.");
        }

        foreach (var keyword in _options.BlockedKeywords)
        {
            var keywordPattern = $@"\b{Regex.Escape(keyword)}\b";
            if (Regex.IsMatch(upperSql, keywordPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                errors.Add($"Blocked keyword detected: {keyword}.");
            }
        }

        if (MultiStatementRegex.IsMatch(normalizedSql))
        {
            errors.Add("Multiple SQL statements are not allowed.");
        }

        foreach (var pattern in _options.SuspiciousPatterns)
        {
            if (Regex.IsMatch(normalizedSql, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                suspiciousMatches.Add(pattern);
            }
        }

        return errors.Count > 0
            ? SqlValidationResult.Invalid(errors, suspiciousMatches)
            : SqlValidationResult.Valid(suspiciousMatches);
    }
}

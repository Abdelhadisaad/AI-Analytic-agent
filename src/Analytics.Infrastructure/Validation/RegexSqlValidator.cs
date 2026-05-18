using System.Text.RegularExpressions;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Options;

namespace Analytics.Infrastructure.Validation;

/// <summary>
/// Regex-based SQL validator that uses pattern matching to detect blocked
/// keywords and suspicious constructs in generated SQL.
///
/// <para><b>Known limitations (OWASP 2021)</b></para>
/// <para>
/// Regex-based SQL validation is inherently limited and fragile. The following
/// risks are explicitly acknowledged:
/// </para>
/// <list type="bullet">
///   <item><b>False positives</b> — Blocked keywords appearing inside string
///     literals, column names, or aliases are incorrectly flagged. For example,
///     <c>WHERE action = 'DELETE'</c> is rejected even though <c>DELETE</c>
///     is a data value, not a SQL command.</item>
///   <item><b>Evasion via encoding</b> — Attackers can bypass keyword detection
///     using Unicode substitution, hex encoding, or double-encoding of
///     characters that visually resemble blocked keywords.</item>
///   <item><b>Evasion via comment injection</b> — Inserting SQL comments within
///     keywords (e.g. <c>DR/**/OP</c>) can split the keyword across the
///     comment boundary, bypassing word-boundary regex matching.</item>
///   <item><b>No structural understanding</b> — Regex operates on the raw string
///     and cannot distinguish between a top-level statement and a subquery,
///     a keyword and an identifier, or a command and a string literal.</item>
///   <item><b>Dialect-unaware</b> — The patterns are not specific to any SQL
///     dialect and may miss dialect-specific dangerous constructs.</item>
/// </list>
///
/// <para><b>Role in defense-in-depth</b></para>
/// <para>
/// This validator is now used as a <em>secondary heuristic layer</em> within
/// <see cref="CompositeSqlValidator"/>. The primary structural validation is
/// performed by <see cref="AstSqlValidator"/> which parses SQL into an
/// Abstract Syntax Tree, eliminating the false-positive and evasion risks
/// listed above. This regex validator contributes suspicious pattern
/// detection (comments, UNION, system catalog access) as advisory warnings.
/// </para>
///
/// <para><b>References</b></para>
/// <list type="bullet">
///   <item>OWASP (2021) — SQL Injection Prevention Cheat Sheet</item>
///   <item>OWASP (2021) — Query Parameterization Cheat Sheet</item>
/// </list>
/// </summary>
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

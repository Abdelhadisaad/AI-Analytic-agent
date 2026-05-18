using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Logging;

namespace Analytics.Infrastructure.Validation;

/// <summary>
/// Defense-in-depth SQL validator that combines AST-based structural validation
/// with regex-based heuristic pattern detection.
///
/// <para><b>Validation strategy</b></para>
/// <list type="number">
///   <item><b>AST validation (primary)</b> — Parses the SQL into an Abstract Syntax
///     Tree using the PostgreSQL dialect. Verifies that the query is syntactically
///     valid, contains exactly one statement, and that statement is a SELECT.
///     This catches structural violations that regex cannot detect.</item>
///   <item><b>Regex suspicious pattern detection (advisory)</b> — Scans the raw SQL
///     string for heuristic patterns that may indicate injection attempts:
///     SQL comments (<c>--</c>, <c>/*</c>), <c>UNION</c>, <c>PG_SLEEP</c>,
///     <c>INFORMATION_SCHEMA</c>, <c>PG_CATALOG</c>. These are reported as
///     suspicious patterns (warnings) even if the AST validates the query.</item>
///   <item><b>Fallback</b> — If the AST parser throws an unexpected exception
///     (library bug, unsupported syntax), the validator falls back to the
///     full regex validation to maintain availability.</item>
/// </list>
///
/// <para><b>Why defense-in-depth?</b></para>
/// <para>
/// Following OWASP (2021) robust validation principles, we layer multiple
/// validation mechanisms so that a bypass of one layer is caught by another.
/// The AST parser provides structural guarantees, while the regex patterns
/// add heuristic detection for suspicious constructs that are syntactically
/// valid but semantically dangerous (e.g. UNION-based data exfiltration).
/// </para>
///
/// <para><b>References</b></para>
/// <list type="bullet">
///   <item>OWASP (2021) — SQL Injection Prevention Cheat Sheet, robust validation principles</item>
///   <item>NIST AI RMF (2023) — Measure 2.6: pre-deployment testing for AI systems</item>
/// </list>
/// </summary>
public sealed class CompositeSqlValidator : ISqlValidator
{
    private readonly AstSqlValidator _astValidator;
    private readonly RegexSqlValidator _regexValidator;
    private readonly ILogger<CompositeSqlValidator> _logger;

    public CompositeSqlValidator(
        AstSqlValidator astValidator,
        RegexSqlValidator regexValidator,
        ILogger<CompositeSqlValidator> logger)
    {
        _astValidator = astValidator;
        _regexValidator = regexValidator;
        _logger = logger;
    }

    public SqlValidationResult Validate(string sql)
    {
        // ── Layer 1: AST structural validation ─────────────────
        SqlValidationResult astResult;
        try
        {
            astResult = _astValidator.Validate(sql);
        }
        catch (Exception ex)
        {
            // AST parser threw an unexpected exception — fall back to regex entirely.
            _logger.LogWarning(ex, "AST validator threw unexpected exception, falling back to regex validation");
            return _regexValidator.Validate(sql);
        }

        // If AST says invalid, reject immediately — structural issues are authoritative.
        if (!astResult.IsValid)
        {
            _logger.LogInformation("SQL rejected by AST validation: {Errors}", string.Join("; ", astResult.Errors));
            return astResult;
        }

        // ── Layer 2: Regex suspicious pattern detection ────────
        // AST passed structural checks, but we still want to detect
        // heuristic patterns that indicate potential injection attempts.
        var regexResult = _regexValidator.Validate(sql);

        // Collect suspicious patterns from regex (regardless of regex validity).
        var suspiciousPatterns = regexResult.SuspiciousPatterns.ToList();

        if (suspiciousPatterns.Count > 0)
        {
            _logger.LogWarning(
                "SQL passed AST validation but has suspicious patterns: {Patterns}",
                string.Join(", ", suspiciousPatterns));
        }

        // AST is authoritative for structural validity. If the regex reports
        // errors (e.g. blocked keyword in a string literal), we trust the AST.
        // Only suspicious patterns are propagated as warnings.
        return SqlValidationResult.Valid(suspiciousPatterns.Count > 0 ? suspiciousPatterns : null);
    }
}

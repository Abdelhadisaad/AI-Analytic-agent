using Analytics.Infrastructure.Validation;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Analytics.UnitTests.Validation;

/// <summary>
/// Tests for <see cref="CompositeSqlValidator"/> — the defense-in-depth
/// validator that combines AST structural validation with regex heuristic
/// pattern detection.
///
/// Key behaviours tested:
/// 1. AST is authoritative for structural validity (overrides regex false positives)
/// 2. Regex suspicious patterns are still reported as warnings
/// 3. Dangerous statements are caught by AST even without regex
/// 4. Both layers work together for defense-in-depth
/// </summary>
public class CompositeSqlValidatorTests
{
    private readonly CompositeSqlValidator _sut;

    public CompositeSqlValidatorTests()
    {
        var astLogger = new Mock<ILogger<AstSqlValidator>>();
        var regexOptions = Options.Create(new SqlValidationOptions());
        var compositeLogger = new Mock<ILogger<CompositeSqlValidator>>();

        var astValidator = new AstSqlValidator(astLogger.Object);
        var regexValidator = new RegexSqlValidator(regexOptions);

        _sut = new CompositeSqlValidator(astValidator, regexValidator, compositeLogger.Object);
    }

    // ── Valid queries pass both layers ────────────────────────

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT id, name FROM orders WHERE status = 'active'")]
    [InlineData("SELECT COUNT(*) FROM products LIMIT 10")]
    public void Validate_ValidSelectQueries_ReturnsValid(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ── AST overrides regex false positives ───────────────────

    [Fact]
    public void Validate_BlockedKeywordInStringLiteral_AstOverridesRegexFalsePositive()
    {
        // Regex would reject this (DELETE found), but AST correctly identifies
        // it as a valid SELECT with DELETE as a string literal value.
        var sql = "SELECT * FROM users WHERE action = 'DELETE'";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue(
            "because AST is authoritative and correctly identifies this as a SELECT. " +
            "The regex false positive (blocked keyword in string literal) is overridden.");
    }

    // ── Dangerous statements rejected by AST ──────────────────

    [Theory]
    [InlineData("INSERT INTO users VALUES (1, 'test')")]
    [InlineData("DELETE FROM users WHERE id = 1")]
    [InlineData("DROP TABLE users")]
    [InlineData("UPDATE users SET name = 'hacked'")]
    public void Validate_DangerousStatements_RejectedByAst(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not allowed"));
    }

    // ── Multi-statement injection caught by AST ──────────────

    [Theory]
    [InlineData("SELECT * FROM users; DELETE FROM users")]
    [InlineData("SELECT 1; DROP TABLE foo")]
    public void Validate_MultiStatementInjection_RejectedByAst(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Multiple"));
    }

    // ── Suspicious patterns reported as warnings ──────────────

    [Fact]
    public void Validate_SelectWithUnion_ReportsSuspiciousPatterns()
    {
        // UNION queries are syntactically valid SELECTs but may indicate
        // injection attempts — reported as suspicious pattern warnings.
        var sql = "SELECT id FROM users UNION SELECT id FROM admins";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue("because AST identifies it as a valid SELECT");
        result.SuspiciousPatterns.Should().NotBeEmpty("because UNION is a suspicious heuristic pattern");
    }

    [Fact]
    public void Validate_SelectWithInformationSchema_ReportsSuspiciousPatterns()
    {
        var sql = "SELECT * FROM information_schema.tables LIMIT 10";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue("because AST identifies it as a valid SELECT");
        result.SuspiciousPatterns.Should().NotBeEmpty("because INFORMATION_SCHEMA access is suspicious");
    }

    // ── Empty queries rejected ────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyQueries_RejectedByAst(string? sql)
    {
        var result = _sut.Validate(sql!);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("empty"));
    }
}

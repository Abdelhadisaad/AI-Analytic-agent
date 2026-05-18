using Analytics.Infrastructure.Validation;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Analytics.UnitTests.Validation;

/// <summary>
/// Tests for <see cref="AstSqlValidator"/> — the AST-based SQL validator
/// that uses SqlParser-cs with PostgreSQL dialect to structurally validate
/// generated SQL queries.
///
/// These tests demonstrate:
/// 1. Structural validation catches dangerous statements (INSERT, DROP, etc.)
/// 2. False positives from regex are eliminated (blocked keywords in string literals)
/// 3. Multi-statement injection is caught at the AST level
/// 4. Syntactically invalid SQL is rejected
/// </summary>
public class AstSqlValidatorTests
{
    private readonly AstSqlValidator _sut;

    public AstSqlValidatorTests()
    {
        var logger = new Mock<ILogger<AstSqlValidator>>();
        _sut = new AstSqlValidator(logger.Object);
    }

    // ── Valid SELECT queries ──────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT id, name FROM orders WHERE status = 'active'")]
    [InlineData("SELECT COUNT(*) FROM products")]
    [InlineData("  SELECT * FROM items  ")]
    [InlineData("SELECT * FROM users LIMIT 100")]
    [InlineData("SELECT a.id, b.name FROM orders a JOIN customers b ON a.customer_id = b.id")]
    [InlineData("SELECT * FROM (SELECT 1 AS x) sub LIMIT 5")]
    public void Validate_ValidSelectQueries_ReturnsValid(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ── Empty / null ──────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyOrWhitespaceSql_ReturnsInvalid(string? sql)
    {
        var result = _sut.Validate(sql!);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("empty"));
    }

    // ── Blocked statement types (structural) ──────────────────

    [Theory]
    [InlineData("INSERT INTO users VALUES (1, 'test')", "INSERT")]
    [InlineData("UPDATE users SET name = 'test'", "UPDATE")]
    [InlineData("DELETE FROM users WHERE id = 1", "DELETE")]
    [InlineData("DROP TABLE users", "DROP")]
    [InlineData("TRUNCATE TABLE users", "TRUNCATE")]
    [InlineData("CREATE TABLE test (id INT)", "CREATE TABLE")]
    public void Validate_DangerousStatementTypes_ReturnsInvalid(string sql, string expectedLabel)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not allowed"));
    }

    // ── Multi-statement injection ─────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM users; DELETE FROM users")]
    [InlineData("SELECT * FROM users; DROP TABLE users")]
    [InlineData("SELECT 1; SELECT 2")]
    public void Validate_MultipleStatements_ReturnsInvalid(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Multiple"));
    }

    // ── Syntactically invalid SQL ─────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM; broken")]
    [InlineData("SELCT * FROM users")]
    [InlineData("SELECT FROM")]
    public void Validate_SyntacticallyInvalidSql_ReturnsInvalid(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("syntax error") || e.Contains("tokenization error"));
    }

    // ── KEY IMPROVEMENT: False positives eliminated ───────────
    //
    // This is the critical test that demonstrates why AST validation
    // is superior to regex. The regex validator incorrectly rejects
    // this query because "DELETE" appears as a string literal value.
    // The AST parser correctly identifies the overall statement as
    // a SELECT and allows it through.

    [Fact]
    public void Validate_BlockedKeywordInStringLiteral_ReturnsValid_UnlikeRegex()
    {
        // This query is a valid SELECT — "DELETE" is a data value, not a command.
        // The regex validator would reject this (false positive).
        // The AST validator correctly allows it.
        var sql = "SELECT * FROM users WHERE action = 'DELETE'";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue(
            "because 'DELETE' inside a string literal is a data value, not a SQL command — " +
            "this demonstrates the AST validator's advantage over regex-based validation");
    }

    [Fact]
    public void Validate_BlockedKeywordInsert_InColumnAlias_ReturnsValid()
    {
        // "INSERT" as a column alias is harmless — AST knows it's not a statement.
        var sql = "SELECT status AS insert_status FROM orders LIMIT 10";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue(
            "because 'insert' in a column alias is not a dangerous statement");
    }

    [Fact]
    public void Validate_SelectWithSubquery_ReturnsValid()
    {
        var sql = "SELECT * FROM (SELECT id, name FROM customers WHERE country = 'NL') sub LIMIT 10";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SelectWithCommonTableExpression_ReturnsValid()
    {
        var sql = "WITH active_orders AS (SELECT * FROM orders WHERE status = 'active') SELECT * FROM active_orders LIMIT 10";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue();
    }

    // ── SQL comment handling (AST parses through comments) ────

    [Fact]
    public void Validate_SelectWithLineComment_ReturnsValid()
    {
        // The AST parser correctly handles SQL comments as part of syntax.
        var sql = "SELECT * FROM users -- this is a comment";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue(
            "because AST correctly parses the comment as non-executable syntax");
    }

    [Fact]
    public void Validate_SelectWithBlockComment_ReturnsValid()
    {
        var sql = "SELECT /* comment */ * FROM users";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue();
    }
}

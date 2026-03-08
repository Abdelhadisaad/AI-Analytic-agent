using Analytics.Infrastructure.Validation;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Analytics.UnitTests.Validation;

public class RegexSqlValidatorTests
{
    private readonly RegexSqlValidator _sut;

    public RegexSqlValidatorTests()
    {
        var options = Options.Create(new SqlValidationOptions());
        _sut = new RegexSqlValidator(options);
    }

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT id, name FROM orders WHERE status = 'active'")]
    [InlineData("SELECT COUNT(*) FROM products")]
    [InlineData("  SELECT * FROM items  ")]
    public void Validate_ValidSelectQueries_ReturnsValid(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

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

    [Theory]
    [InlineData("INSERT INTO users VALUES (1, 'test')")]
    [InlineData("UPDATE users SET name = 'test'")]
    [InlineData("DELETE FROM users WHERE id = 1")]
    [InlineData("DROP TABLE users")]
    [InlineData("ALTER TABLE users ADD COLUMN x INT")]
    [InlineData("TRUNCATE TABLE users")]
    [InlineData("CREATE TABLE test (id INT)")]
    public void Validate_BlockedKeywords_ReturnsInvalid(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("SELECT * FROM users; DELETE FROM users")]
    [InlineData("SELECT * FROM users;DROP TABLE users")]
    [InlineData("SELECT 1; SELECT 2")]
    public void Validate_MultipleStatements_ReturnsInvalid(string sql)
    {
        var result = _sut.Validate(sql);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Multiple"));
    }

    [Fact]
    public void Validate_SelectWithBlockedKeywordInData_ReturnsInvalid()
    {
        var sql = "SELECT * FROM users WHERE action = 'DELETE'";

        var result = _sut.Validate(sql);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("DELETE"));
    }

    [Theory]
    [InlineData("SELECT * FROM users -- comment")]
    [InlineData("SELECT * FROM users /* comment */")]
    public void Validate_SqlComments_ReturnsSuspiciousPatterns(string sql)
    {
        var result = _sut.Validate(sql);

        result.SuspiciousPatterns.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("SELECT * FROM users UNION SELECT * FROM admins")]
    [InlineData("SELECT * FROM information_schema.tables")]
    [InlineData("SELECT * FROM pg_catalog.pg_tables")]
    public void Validate_InjectionPatterns_ReturnsSuspiciousPatterns(string sql)
    {
        var result = _sut.Validate(sql);

        result.SuspiciousPatterns.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_WithCustomBlockedKeywords_BlocksCustomKeywords()
    {
        var options = Options.Create(new SqlValidationOptions
        {
            BlockedKeywords = new[] { "CUSTOM_BLOCKED" }
        });
        var validator = new RegexSqlValidator(options);

        var result = validator.Validate("SELECT CUSTOM_BLOCKED FROM table");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("CUSTOM_BLOCKED"));
    }

    [Fact]
    public void Validate_WithCustomSuspiciousPatterns_DetectsPatterns()
    {
        var options = Options.Create(new SqlValidationOptions
        {
            BlockedKeywords = Array.Empty<string>(),
            SuspiciousPatterns = new[] { @"DANGER" }
        });
        var validator = new RegexSqlValidator(options);

        var result = validator.Validate("SELECT * FROM DANGER_TABLE");

        result.IsValid.Should().BeTrue();
        result.SuspiciousPatterns.Should().Contain("DANGER");
    }
}

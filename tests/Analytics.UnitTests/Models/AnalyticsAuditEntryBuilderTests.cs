using Analytics.Application.Models;
using FluentAssertions;
using Xunit;

namespace Analytics.UnitTests.Models;

public class AnalyticsAuditEntryBuilderTests
{
    [Fact]
    public void Build_WithMinimalProperties_ReturnsValidEntry()
    {
        var builder = new AnalyticsAuditEntryBuilder();

        var entry = builder
            .WithCorrelationId("corr-123")
            .WithRequestId("req-456")
            .WithUserQuestion("Hoeveel orders?")
            .Build();

        entry.CorrelationId.Should().Be("corr-123");
        entry.RequestId.Should().Be("req-456");
        entry.UserQuestion.Should().Be("Hoeveel orders?");
        entry.EntryId.Should().NotBeNullOrEmpty();
        entry.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Build_WithLocale_SetsLocaleProperty()
    {
        var entry = new AnalyticsAuditEntryBuilder()
            .WithUserQuestion("Test", "nl-NL")
            .Build();

        entry.Locale.Should().Be("nl-NL");
    }

    [Fact]
    public void Build_WithAiResponse_SetsAllAiProperties()
    {
        var tables = new[] { "orders", "customers" };
        var columns = new[] { "id", "name", "created_at" };

        var entry = new AnalyticsAuditEntryBuilder()
            .WithAiResponse(
                sql: "SELECT * FROM orders",
                confidence: 0.95,
                tables: tables,
                columns: columns,
                durationMs: 150)
            .Build();

        entry.GeneratedSql.Should().Be("SELECT * FROM orders");
        entry.ConfidenceScore.Should().Be(0.95);
        entry.SelectedTables.Should().BeEquivalentTo(tables);
        entry.SelectedColumns.Should().BeEquivalentTo(columns);
        entry.AiServiceDurationMs.Should().Be(150);
    }

    [Theory]
    [InlineData(true, new string[0], ValidationOutcome.Passed)]
    [InlineData(false, new[] { "Syntax error" }, ValidationOutcome.Failed)]
    public void Build_WithValidationResult_SetsCorrectOutcome(
        bool isValid,
        string[] errors,
        ValidationOutcome expectedOutcome)
    {
        var validationResult = isValid
            ? SqlValidationResult.Valid()
            : SqlValidationResult.Invalid(errors);

        var entry = new AnalyticsAuditEntryBuilder()
            .WithValidationResult(validationResult, 10)
            .Build();

        entry.ValidationResult.Should().Be(expectedOutcome);
        entry.ValidationDurationMs.Should().Be(10);
    }

    [Fact]
    public void Build_WithBlockedKeyword_SetsUnsafeBlockedOutcome()
    {
        var validationResult = SqlValidationResult.Invalid(
            new[] { "Blocked keyword: DELETE" });

        var entry = new AnalyticsAuditEntryBuilder()
            .WithValidationResult(validationResult, 5)
            .Build();

        entry.ValidationResult.Should().Be(ValidationOutcome.UnsafeBlocked);
    }

    [Fact]
    public void Build_WithSuspiciousPatterns_SetsPassedWithWarningsOutcome()
    {
        var validationResult = SqlValidationResult.Valid(
            suspiciousPatterns: new[] { "UNION detected" });

        var entry = new AnalyticsAuditEntryBuilder()
            .WithValidationResult(validationResult, 8)
            .Build();

        entry.ValidationResult.Should().Be(ValidationOutcome.PassedWithWarnings);
    }

    [Theory]
    [InlineData(false, ExecutionOutcome.Success)]
    [InlineData(true, ExecutionOutcome.SuccessTruncated)]
    public void Build_WithExecutionSuccess_SetsCorrectOutcome(bool truncated, ExecutionOutcome expected)
    {
        var entry = new AnalyticsAuditEntryBuilder()
            .WithExecutionSuccess(100, truncated)
            .Build();

        entry.ExecutionStatus.Should().Be(expected);
        entry.ExecutionDurationMs.Should().Be(100);
    }

    [Fact]
    public void Build_WithExecutionFailure_SetsErrorInfo()
    {
        var entry = new AnalyticsAuditEntryBuilder()
            .WithExecutionFailure(
                ExecutionOutcome.PermissionDenied,
                errorMessage: "Access denied",
                errorType: "PermissionException")
            .Build();

        entry.ExecutionStatus.Should().Be(ExecutionOutcome.PermissionDenied);
        entry.ErrorMessage.Should().Be("Access denied");
        entry.ErrorType.Should().Be("PermissionException");
    }

    [Fact]
    public void Build_WithAiServiceFailure_SetsFailureInfo()
    {
        var entry = new AnalyticsAuditEntryBuilder()
            .WithAiServiceFailure(AiServiceFailureType.Timeout, "Request timed out")
            .Build();

        entry.ExecutionStatus.Should().Be(ExecutionOutcome.AiServiceFailed);
        entry.ErrorMessage.Should().Be("Request timed out");
        entry.ErrorType.Should().Be("Timeout");
    }

    [Fact]
    public void Build_WithValidationBlocked_SetsBlockedOutcome()
    {
        var entry = new AnalyticsAuditEntryBuilder()
            .WithValidationBlocked()
            .Build();

        entry.ExecutionStatus.Should().Be(ExecutionOutcome.ValidationBlocked);
    }

    [Fact]
    public void Build_CalculatesTotalDuration()
    {
        var entry = new AnalyticsAuditEntryBuilder()
            .WithCorrelationId("test")
            .Build();

        entry.TotalDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void Build_GeneratesUniqueEntryIds()
    {
        var entry1 = new AnalyticsAuditEntryBuilder().Build();
        var entry2 = new AnalyticsAuditEntryBuilder().Build();

        entry1.EntryId.Should().NotBe(entry2.EntryId);
    }

    [Fact]
    public void Build_FluentChaining_WorksCorrectly()
    {
        var entry = new AnalyticsAuditEntryBuilder()
            .WithCorrelationId("corr")
            .WithRequestId("req")
            .WithUserQuestion("Query?", "en-US")
            .WithAiResponse("SELECT 1", 0.99, null, null, 50)
            .WithValidationResult(SqlValidationResult.Valid(), 5)
            .WithExecutionSuccess(25, false)
            .Build();

        entry.CorrelationId.Should().Be("corr");
        entry.RequestId.Should().Be("req");
        entry.UserQuestion.Should().Be("Query?");
        entry.Locale.Should().Be("en-US");
        entry.GeneratedSql.Should().Be("SELECT 1");
        entry.ConfidenceScore.Should().Be(0.99);
        entry.AiServiceDurationMs.Should().Be(50);
        entry.ValidationResult.Should().Be(ValidationOutcome.Passed);
        entry.ValidationDurationMs.Should().Be(5);
        entry.ExecutionStatus.Should().Be(ExecutionOutcome.Success);
        entry.ExecutionDurationMs.Should().Be(25);
    }

    [Fact]
    public void Build_DefaultValues_AreSetCorrectly()
    {
        var entry = new AnalyticsAuditEntryBuilder().Build();

        entry.ValidationResult.Should().Be(ValidationOutcome.NotPerformed);
        entry.ExecutionStatus.Should().Be(ExecutionOutcome.NotPerformed);
        entry.CorrelationId.Should().BeEmpty();
        entry.RequestId.Should().BeEmpty();
        entry.UserQuestion.Should().BeEmpty();
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    [InlineData("DROP")]
    public void Build_WithVariousBlockedKeywords_AllDetectedAsUnsafe(string keyword)
    {
        var validationResult = SqlValidationResult.Invalid(
            new[] { $"Query contains blocked keyword: {keyword}" });

        var entry = new AnalyticsAuditEntryBuilder()
            .WithValidationResult(validationResult, 5)
            .Build();

        entry.ValidationResult.Should().Be(ValidationOutcome.UnsafeBlocked);
    }
}

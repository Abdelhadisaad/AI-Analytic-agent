using Analytics.Application.Models;
using Analytics.Infrastructure.Fallback;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Analytics.UnitTests.Fallback;

public class DefaultFallbackHandlerTests
{
    private readonly Mock<ILogger<DefaultFallbackHandler>> _loggerMock;
    private readonly DefaultFallbackHandler _sut;

    public DefaultFallbackHandlerTests()
    {
        _loggerMock = new Mock<ILogger<DefaultFallbackHandler>>();
        _sut = new DefaultFallbackHandler(_loggerMock.Object);
    }

    [Fact]
    public void HandleAiServiceFailure_Timeout_ReturnsTimeoutFallback()
    {
        var result = _sut.HandleAiServiceFailure(
            AiServiceFailureType.Timeout,
            "Request timed out",
            "Hoeveel orders zijn er?");

        result.Reason.Should().Be(FallbackReason.AiServiceTimeout);
        result.IsRetryable.Should().BeTrue();
        result.Message.Should().Contain("niet op tijd");
    }

    [Theory]
    [InlineData(AiServiceFailureType.Unreachable)]
    [InlineData(AiServiceFailureType.ServiceError)]
    public void HandleAiServiceFailure_ServiceUnavailable_ReturnsUnavailableFallback(AiServiceFailureType failureType)
    {
        var result = _sut.HandleAiServiceFailure(
            failureType,
            "Service error",
            "Test query");

        result.Reason.Should().Be(FallbackReason.AiServiceUnavailable);
        result.IsRetryable.Should().BeTrue();
        result.Message.Should().Contain("niet beschikbaar");
    }

    [Fact]
    public void HandleAiServiceFailure_InvalidResponse_ReturnsInvalidResponseFallback()
    {
        var result = _sut.HandleAiServiceFailure(
            AiServiceFailureType.InvalidResponse,
            "Could not parse JSON",
            "Test query");

        result.Reason.Should().Be(FallbackReason.InvalidAiResponse);
        result.IsRetryable.Should().BeTrue();
        result.Message.Should().Contain("ongeldig antwoord");
    }

    [Fact]
    public void HandleAiServiceFailure_RetriesExhausted_ReturnsNonRetryableFallback()
    {
        var result = _sut.HandleAiServiceFailure(
            AiServiceFailureType.RetriesExhausted,
            "Max retries exceeded",
            "Test query");

        result.Reason.Should().Be(FallbackReason.AiServiceUnavailable);
        result.IsRetryable.Should().BeFalse();
        result.SuggestedAction.Should().Contain("contact");
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("DELETE")]
    [InlineData("DROP")]
    [InlineData("UPDATE")]
    [InlineData("ALTER")]
    [InlineData("TRUNCATE")]
    public void HandleValidationFailure_BlockedKeyword_ReturnsUnsafeSqlFallback(string keyword)
    {
        var validationResult = SqlValidationResult.Invalid(
            new[] { $"Blocked keyword detected: {keyword}" });

        var result = _sut.HandleValidationFailure(
            validationResult,
            $"{keyword} FROM users",
            "Test query");

        result.Reason.Should().Be(FallbackReason.UnsafeSqlDetected);
        result.IsRetryable.Should().BeTrue();
        result.ValidationErrors.Should().NotBeEmpty();
        result.Message.Should().Contain("niet zijn toegestaan");
    }

    [Fact]
    public void HandleValidationFailure_SuspiciousPatterns_ReturnsSuspiciousFallback()
    {
        var validationResult = SqlValidationResult.Valid(
            new[] { "Stacked queries detected" });
        var invalidWithPatterns = SqlValidationResult.Invalid(
            Array.Empty<string>(),
            new[] { "Stacked queries detected" });

        var result = _sut.HandleValidationFailure(
            invalidWithPatterns,
            "SELECT 1; SELECT 2",
            "Test query");

        result.Reason.Should().Be(FallbackReason.SuspiciousPatternsDetected);
        result.IsRetryable.Should().BeFalse();
        result.SuspiciousPatterns.Should().NotBeEmpty();
    }

    [Fact]
    public void HandleValidationFailure_GenericError_ReturnsValidationFailedFallback()
    {
        var validationResult = SqlValidationResult.Invalid(
            new[] { "SQL syntax error" });

        var result = _sut.HandleValidationFailure(
            validationResult,
            "SELEC * FROM users",
            "Test query");

        result.Reason.Should().Be(FallbackReason.SqlValidationFailed);
        result.IsRetryable.Should().BeTrue();
        result.ValidationErrors.Should().Contain("SQL syntax error");
    }

    [Fact]
    public void HandleDatabaseFailure_PermissionDenied_ReturnsNonRetryableFallback()
    {
        var exception = new Exception("permission denied for table users");

        var result = _sut.HandleDatabaseFailure(
            exception,
            "SELECT * FROM users",
            "Show users");

        result.Reason.Should().Be(FallbackReason.DatabaseExecutionFailed);
        result.IsRetryable.Should().BeFalse();
        result.Message.Should().Contain("beveiligingsbeperkingen");
    }

    [Fact]
    public void HandleDatabaseFailure_ReadOnly_ReturnsNonRetryableFallback()
    {
        var exception = new Exception("cannot execute INSERT in a read-only transaction");

        var result = _sut.HandleDatabaseFailure(
            exception,
            "INSERT INTO users VALUES (1)",
            "Add user");

        result.Reason.Should().Be(FallbackReason.DatabaseExecutionFailed);
        result.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void HandleDatabaseFailure_Timeout_ReturnsRetryableFallback()
    {
        var exception = new Exception("canceling statement due to timeout");

        var result = _sut.HandleDatabaseFailure(
            exception,
            "SELECT * FROM large_table",
            "Show all data");

        result.Reason.Should().Be(FallbackReason.DatabaseExecutionFailed);
        result.IsRetryable.Should().BeTrue();
        result.Message.Should().Contain("te lang");
    }

    [Fact]
    public void HandleDatabaseFailure_TableNotExists_ReturnsRetryableFallback()
    {
        var exception = new Exception("relation \"products\" does not exist");

        var result = _sut.HandleDatabaseFailure(
            exception,
            "SELECT * FROM products",
            "Show products");

        result.Reason.Should().Be(FallbackReason.DatabaseExecutionFailed);
        result.IsRetryable.Should().BeTrue();
        result.Message.Should().Contain("niet bestaat");
    }

    [Fact]
    public void HandleDatabaseFailure_GenericError_ReturnsRetryableFallback()
    {
        var exception = new Exception("Unknown database error");

        var result = _sut.HandleDatabaseFailure(
            exception,
            "SELECT * FROM users",
            "Test query");

        result.Reason.Should().Be(FallbackReason.DatabaseExecutionFailed);
        result.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void HandleAiServiceFailure_LogsWarning()
    {
        _sut.HandleAiServiceFailure(
            AiServiceFailureType.Timeout,
            "Timed out",
            "Test query");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception?>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void HandleDatabaseFailure_LogsError()
    {
        var exception = new Exception("DB error");

        _sut.HandleDatabaseFailure(exception, "SELECT 1", "Test");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                exception,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }
}

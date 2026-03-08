using System.Net;
using System.Text.Json;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Analytics.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Analytics.IntegrationTests.AiService;

public class AiServiceClientIntegrationTests : IAsyncLifetime
{
    private WireMockServer _mockServer = null!;
    private IAiServiceClient _sut = null!;
    private HttpClient _httpClient = null!;

    public Task InitializeAsync()
    {
        _mockServer = WireMockServer.Start();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Url!),
            Timeout = TimeSpan.FromSeconds(10)
        };

        var loggerMock = new Mock<ILogger<AiServiceClient>>();
        _sut = new AiServiceClient(_httpClient, loggerMock.Object);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        _mockServer.Stop();
        _mockServer.Dispose();
        return Task.CompletedTask;
    }

    #region Success Scenarios

    [Fact]
    public async Task GenerateSqlAsync_ValidRequest_ReturnsSuccessfulResponse()
    {
        // Arrange
        var request = CreateValidRequest();
        var expectedResponse = CreateValidResponse(request.RequestId, request.CorrelationId);

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedResponse, JsonOptions)));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.SqlProposal.Sql.Should().Be("SELECT COUNT(*) FROM orders");
        result.Response.ExplanationMetadata.ConfidenceScore.Should().Be(0.95);
    }

    [Fact]
    public async Task GenerateSqlAsync_ValidRequest_PreservesCorrelationId()
    {
        // Arrange
        var request = CreateValidRequest();
        var expectedResponse = CreateValidResponse(request.RequestId, request.CorrelationId);

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(expectedResponse, JsonOptions)));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.Response!.CorrelationId.Should().Be(request.CorrelationId);
        result.Response.RequestId.Should().Be(request.RequestId);
    }

    [Fact]
    public async Task GenerateSqlAsync_ResponseWithParameters_DeserializesParametersCorrectly()
    {
        // Arrange
        var request = CreateValidRequest();
        var response = new GenerateSqlResponse
        {
            RequestId = request.RequestId,
            CorrelationId = request.CorrelationId,
            SqlProposal = new SqlProposal
            {
                Dialect = "postgresql",
                Sql = "SELECT * FROM orders WHERE status = @status AND created_at > @since",
                Parameters = new List<SqlParameter>
                {
                    new() { Name = "@status", Value = "completed" },
                    new() { Name = "@since", Value = "2026-01-01" }
                }
            },
            ExplanationMetadata = CreateExplanationMetadata()
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(response, JsonOptions)));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Response!.SqlProposal.Parameters.Should().HaveCount(2);
        result.Response.SqlProposal.Parameters[0].Name.Should().Be("@status");
        result.Response.SqlProposal.Parameters[0].Value?.ToString().Should().Be("completed");
    }

    #endregion

    #region Timeout Scenarios

    [Fact]
    public async Task GenerateSqlAsync_ServiceRespondsSlow_ReturnsTimeoutFailure()
    {
        // Arrange
        var request = CreateValidRequest();

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithDelay(TimeSpan.FromSeconds(15))); // Longer than client timeout

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(AiServiceFailureType.Timeout);
        result.FailureMessage.Should().Contain("timeout");
    }

    #endregion

    #region Service Unavailable Scenarios

    [Fact]
    public async Task GenerateSqlAsync_ServiceReturns500_ReturnsServiceErrorFailure()
    {
        // Arrange
        var request = CreateValidRequest();

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.InternalServerError)
                .WithBody("Internal Server Error"));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(AiServiceFailureType.ServiceError);
    }

    [Fact]
    public async Task GenerateSqlAsync_ServiceReturns503_ReturnsServiceUnavailable()
    {
        // Arrange
        var request = CreateValidRequest();

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.ServiceUnavailable)
                .WithBody("Service Unavailable"));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().BeOneOf(
            AiServiceFailureType.ServiceError,
            AiServiceFailureType.Unreachable);
    }

    [Fact]
    public async Task GenerateSqlAsync_ServiceUnreachable_ReturnsUnreachableFailure()
    {
        // Arrange
        var request = CreateValidRequest();
        _mockServer.Stop(); // Stop server to simulate unreachable

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(AiServiceFailureType.Unreachable);
        result.FailureMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Invalid Response Scenarios

    [Fact]
    public async Task GenerateSqlAsync_InvalidJsonResponse_ReturnsInvalidResponseFailure()
    {
        // Arrange
        var request = CreateValidRequest();

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{ invalid json }"));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(AiServiceFailureType.InvalidResponse);
    }

    [Fact]
    public async Task GenerateSqlAsync_EmptyResponse_ReturnsInvalidResponseFailure()
    {
        // Arrange
        var request = CreateValidRequest();

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(""));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(AiServiceFailureType.InvalidResponse);
    }

    [Fact]
    public async Task GenerateSqlAsync_MissingSqlProposal_ReturnsInvalidResponseFailure()
    {
        // Arrange
        var request = CreateValidRequest();
        var incompleteResponse = new
        {
            requestId = request.RequestId,
            correlationId = request.CorrelationId
            // Missing sqlProposal
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(incompleteResponse)));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(AiServiceFailureType.InvalidResponse);
    }

    [Fact]
    public async Task GenerateSqlAsync_NullSqlInProposal_ReturnsInvalidResponseFailure()
    {
        // Arrange
        var request = CreateValidRequest();
        var responseWithNullSql = new
        {
            requestId = request.RequestId,
            correlationId = request.CorrelationId,
            sqlProposal = new
            {
                dialect = "postgresql",
                sql = (string?)null,
                parameters = Array.Empty<object>()
            },
            explanationMetadata = new
            {
                intentSummary = "test",
                reasoningSummary = "test",
                assumptions = Array.Empty<string>(),
                selectedTables = Array.Empty<string>(),
                selectedColumns = Array.Empty<string>(),
                confidenceScore = 0.5
            }
        };

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(responseWithNullSql)));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().Be(AiServiceFailureType.InvalidResponse);
    }

    #endregion

    #region HTTP Status Code Scenarios

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GenerateSqlAsync_ClientErrors_ReturnsAppropriateFailure(HttpStatusCode statusCode)
    {
        // Arrange
        var request = CreateValidRequest();

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithBody($"Error: {statusCode}"));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().NotBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task GenerateSqlAsync_GatewayErrors_ReturnsServiceError(HttpStatusCode statusCode)
    {
        // Arrange
        var request = CreateValidRequest();

        _mockServer
            .Given(Request.Create()
                .WithPath("/generate-sql")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithBody($"Gateway Error: {statusCode}"));

        // Act
        var result = await _sut.GenerateSqlAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailureType.Should().BeOneOf(
            AiServiceFailureType.ServiceError,
            AiServiceFailureType.Unreachable);
    }

    #endregion

    #region Helpers

    private static GenerateSqlRequest CreateValidRequest() => new()
    {
        RequestId = Guid.NewGuid().ToString("N"),
        CorrelationId = Guid.NewGuid().ToString("N"),
        NaturalLanguageQuery = "Hoeveel orders zijn er?",
        Locale = "nl-NL",
        SchemaMetadata = new SchemaMetadata
        {
            Dialect = "postgresql",
            Tables = new List<TableMetadata>
            {
                new()
                {
                    TableName = "orders",
                    Description = "Order table",
                    Columns = new List<ColumnMetadata>
                    {
                        new() { ColumnName = "id", DataType = "integer" },
                        new() { ColumnName = "status", DataType = "varchar" },
                        new() { ColumnName = "created_at", DataType = "timestamp" }
                    }
                }
            }
        }
    };

    private static GenerateSqlResponse CreateValidResponse(string requestId, string correlationId) => new()
    {
        RequestId = requestId,
        CorrelationId = correlationId,
        SqlProposal = new SqlProposal
        {
            Dialect = "postgresql",
            Sql = "SELECT COUNT(*) FROM orders",
            Parameters = new List<SqlParameter>()
        },
        ExplanationMetadata = CreateExplanationMetadata()
    };

    private static ExplanationMetadata CreateExplanationMetadata() => new()
    {
        IntentSummary = "Count all orders",
        ReasoningSummary = "User wants total count of orders",
        Assumptions = new List<string> { "All orders regardless of status" },
        SelectedTables = new List<string> { "orders" },
        SelectedColumns = new List<string> { "COUNT(*)" },
        ConfidenceScore = 0.95,
        Warnings = new List<string>()
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #endregion
}

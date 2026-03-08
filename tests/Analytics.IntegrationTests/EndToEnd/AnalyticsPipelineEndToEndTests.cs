using System.Net;
using System.Text.Json;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Analytics.Infrastructure.Fallback;
using Analytics.Infrastructure.Http;
using Analytics.Infrastructure.Validation;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using Xunit.Abstractions;

namespace Analytics.IntegrationTests.EndToEnd;

/// <summary>
/// End-to-end tests covering the full analytics query pipeline flow:
/// Natural Language → AI Service → Validation → Execution → Response
/// 
/// These tests focus on pipeline orchestration logic using:
/// - Real WireMock for AI service simulation
/// - Real SQL validator
/// - Mocked query executor (database integration is tested in PostgreSqlIntegrationTests)
/// </summary>
public class AnalyticsPipelineEndToEndTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly WireMockServer _aiServiceMock;

    private readonly IAiServiceClient _aiServiceClient;
    private readonly ISqlValidator _sqlValidator;
    private readonly Mock<IReadOnlyQueryExecutor> _queryExecutorMock;
    private readonly IFallbackHandler _fallbackHandler;

    public AnalyticsPipelineEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Start WireMock for AI service simulation
        _aiServiceMock = WireMockServer.Start();

        // Setup real AI service client
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_aiServiceMock.Url!),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var aiLoggerMock = new Mock<ILogger<AiServiceClient>>();
        _aiServiceClient = new AiServiceClient(httpClient, aiLoggerMock.Object);

        // Setup real SQL validator
        var validatorOptions = Options.Create(new SqlValidationOptions());
        _sqlValidator = new RegexSqlValidator(validatorOptions);

        // Setup mock query executor
        _queryExecutorMock = new Mock<IReadOnlyQueryExecutor>();

        // Setup real fallback handler
        var fallbackLoggerMock = new Mock<ILogger<DefaultFallbackHandler>>();
        _fallbackHandler = new DefaultFallbackHandler(fallbackLoggerMock.Object);
    }

    public void Dispose()
    {
        _aiServiceMock.Stop();
        _aiServiceMock.Dispose();
    }

    #region Happy Path - Full Pipeline Success

    [Fact]
    public async Task Pipeline_SimpleCountQuery_ReturnsCorrectCount()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var naturalLanguageQuery = "Hoeveel orders zijn er?";
        var sql = "SELECT COUNT(*) as total FROM orders";

        SetupAiServiceResponse(requestId, correlationId, sql);
        SetupSuccessfulQueryExecution(sql, new[] { new Dictionary<string, object?> { ["total"] = 6L } });

        // Act
        var result = await ExecutePipelineAsync(naturalLanguageQuery, correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.Success);
        result.Data.Should().NotBeNull();
        result.Data!.Rows.Should().HaveCount(1);
        result.Data.Rows[0]["total"].Should().Be(6L);
    }

    [Fact]
    public async Task Pipeline_FilteredQuery_ReturnsFilteredResults()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var naturalLanguageQuery = "Welke orders zijn voltooid?";
        var sql = "SELECT id, customer_name, total_amount FROM orders WHERE status = 'completed'";

        SetupAiServiceResponse(requestId, correlationId, sql);
        SetupSuccessfulQueryExecution(sql, new[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["customer_name"] = "Alice", ["total_amount"] = 150.00m },
            new Dictionary<string, object?> { ["id"] = 3, ["customer_name"] = "Bob", ["total_amount"] = 200.00m },
            new Dictionary<string, object?> { ["id"] = 5, ["customer_name"] = "Charlie", ["total_amount"] = 300.00m }
        });

        // Act
        var result = await ExecutePipelineAsync(naturalLanguageQuery, correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.Success);
        result.Data!.Rows.Should().HaveCount(3);
        result.Data.Rows.All(r => r.ContainsKey("customer_name")).Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_AggregateWithGroupBy_ReturnsGroupedResults()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var naturalLanguageQuery = "Hoeveel orders per status?";
        var sql = "SELECT status, COUNT(*) as count FROM orders GROUP BY status ORDER BY status";

        SetupAiServiceResponse(requestId, correlationId, sql);
        SetupSuccessfulQueryExecution(sql, new[]
        {
            new Dictionary<string, object?> { ["status"] = "cancelled", ["count"] = 1L },
            new Dictionary<string, object?> { ["status"] = "completed", ["count"] = 3L },
            new Dictionary<string, object?> { ["status"] = "pending", ["count"] = 2L }
        });

        // Act
        var result = await ExecutePipelineAsync(naturalLanguageQuery, correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.Success);
        result.Data!.Rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task Pipeline_JoinQuery_ReturnsJoinedData()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var naturalLanguageQuery = "Toon orders met klantinformatie";
        var sql = """
            SELECT o.id, o.total_amount, c.name, c.email, c.country
            FROM orders o
            JOIN customers c ON o.customer_id = c.id
            WHERE o.status = 'completed'
            """;

        SetupAiServiceResponse(requestId, correlationId, sql);
        SetupSuccessfulQueryExecution(sql, new[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["total_amount"] = 150.00m, ["name"] = "Alice", ["email"] = "alice@test.com", ["country"] = "NL" },
            new Dictionary<string, object?> { ["id"] = 3, ["total_amount"] = 200.00m, ["name"] = "Bob", ["email"] = "bob@test.com", ["country"] = "US" }
        });

        // Act
        var result = await ExecutePipelineAsync(naturalLanguageQuery, correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.Success);
        result.Data!.Rows.Should().HaveCount(2);
        result.Data.Rows.All(r => r.ContainsKey("email")).Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_NoMatchingRows_ReturnsEmptyResult()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var naturalLanguageQuery = "Toon orders van gisteren";
        var sql = "SELECT * FROM orders WHERE created_at > '2099-01-01'";

        SetupAiServiceResponse(requestId, correlationId, sql);
        SetupSuccessfulQueryExecution(sql, Array.Empty<Dictionary<string, object?>>());

        // Act
        var result = await ExecutePipelineAsync(naturalLanguageQuery, correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.Success);
        result.Data!.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Pipeline_LowConfidenceScore_StillExecutesQuery()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var sql = "SELECT * FROM orders LIMIT 5";

        SetupAiServiceResponseWithConfidence(requestId, correlationId, sql, confidenceScore: 0.45);
        SetupSuccessfulQueryExecution(sql, new[] { new Dictionary<string, object?> { ["id"] = 1 } });

        // Act
        var result = await ExecutePipelineAsync("Ambiguous query", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.Success);
        result.Metadata.ConfidenceScore.Should().Be(0.45);
    }

    #endregion

    #region Malicious Input - SQL Injection Attempts

    [Theory]
    [InlineData("'; DROP TABLE orders; --")]
    [InlineData("1; DELETE FROM customers; --")]
    [InlineData("admin'--")]
    [InlineData("' OR '1'='1")]
    [InlineData("'; INSERT INTO orders VALUES(999, 999, 'hacked', 'evil', 0, NOW()); --")]
    public async Task Pipeline_SqlInjectionInNaturalLanguage_AiRespondsWithMaliciousSql_IsBlocked(string maliciousInput)
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        // AI service "fooled" into generating malicious SQL
        SetupAiServiceResponse(requestId, correlationId,
            $"SELECT * FROM orders WHERE id = {maliciousInput}");

        // Act
        var result = await ExecutePipelineAsync(maliciousInput, correlationId, requestId);

        // Assert
        result.Status.Should().BeOneOf(PipelineStatus.ValidationFailed, PipelineStatus.ExecutionFailed);
        result.Fallback.Should().NotBeNull();
    }

    [Fact]
    public async Task Pipeline_AiGeneratesDropStatement_IsBlocked()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId, "DROP TABLE orders");

        // Act
        var result = await ExecutePipelineAsync("Delete all orders", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
        result.Fallback!.Reason.Should().BeOneOf(
            FallbackReason.SqlValidationFailed,
            FallbackReason.UnsafeSqlDetected);
    }

    [Fact]
    public async Task Pipeline_AiGeneratesDeleteStatement_IsBlocked()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId, "DELETE FROM orders WHERE status = 'cancelled'");

        // Act
        var result = await ExecutePipelineAsync("Remove cancelled orders", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
        result.Fallback!.Reason.Should().BeOneOf(
            FallbackReason.SqlValidationFailed,
            FallbackReason.UnsafeSqlDetected);
    }

    [Fact]
    public async Task Pipeline_AiGeneratesUpdateStatement_IsBlocked()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId,
            "UPDATE orders SET status = 'shipped' WHERE id = 1");

        // Act
        var result = await ExecutePipelineAsync("Mark order 1 as shipped", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
    }

    [Fact]
    public async Task Pipeline_AiGeneratesInsertStatement_IsBlocked()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId,
            "INSERT INTO orders (customer_id, customer_name, status, total_amount) VALUES (1, 'Test', 'new', 100)");

        // Act
        var result = await ExecutePipelineAsync("Add new order", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
    }

    [Fact]
    public async Task Pipeline_AiGeneratesStackedQueries_IsBlocked()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId,
            "SELECT * FROM orders; DELETE FROM customers;");

        // Act
        var result = await ExecutePipelineAsync("Show orders and clean up", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
    }

    [Fact]
    public async Task Pipeline_AiGeneratesUnionInjection_IsBlocked()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId,
            "SELECT id, customer_name FROM orders UNION SELECT id, email FROM customers");

        // Act
        var result = await ExecutePipelineAsync("Show all contact info", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
        result.Fallback!.Reason.Should().Be(FallbackReason.SuspiciousPatternsDetected);
    }

    [Fact]
    public async Task Pipeline_AiGeneratesInformationSchemaAccess_IsBlocked()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId,
            "SELECT table_name FROM information_schema.tables");

        // Act
        var result = await ExecutePipelineAsync("Show database structure", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
        result.Fallback!.Reason.Should().Be(FallbackReason.SuspiciousPatternsDetected);
    }

    [Fact]
    public async Task Pipeline_AiGeneratesPgSleep_IsBlocked()
    {
        // Arrange (time-based SQL injection attempt)
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId,
            "SELECT CASE WHEN (1=1) THEN pg_sleep(10) ELSE 1 END");

        // Act
        var result = await ExecutePipelineAsync("Test performance", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
    }

    #endregion

    #region AI Service Failure Scenarios

    [Fact]
    public async Task Pipeline_AiServiceTimeout_ReturnsFallback()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        _aiServiceMock.Reset();
        _aiServiceMock
            .Given(Request.Create().WithPath("/generate-sql").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithDelay(TimeSpan.FromMinutes(2))); // Way longer than timeout

        // Act
        var result = await ExecutePipelineAsync("Test query", correlationId, requestId,
            timeout: TimeSpan.FromSeconds(2));

        // Assert
        result.Status.Should().Be(PipelineStatus.AiServiceFailed);
        result.Fallback.Should().NotBeNull();
        result.Fallback!.Reason.Should().Be(FallbackReason.AiServiceTimeout);
        result.Fallback.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_AiServiceUnavailable_ReturnsFallback()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        _aiServiceMock.Reset();
        _aiServiceMock
            .Given(Request.Create().WithPath("/generate-sql").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.ServiceUnavailable));

        // Act
        var result = await ExecutePipelineAsync("Test query", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.AiServiceFailed);
        result.Fallback!.Reason.Should().BeOneOf(
            FallbackReason.AiServiceUnavailable,
            FallbackReason.InvalidAiResponse);
    }

    [Fact]
    public async Task Pipeline_AiServiceReturnsInvalidJson_ReturnsFallback()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        _aiServiceMock.Reset();
        _aiServiceMock
            .Given(Request.Create().WithPath("/generate-sql").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{ invalid json }"));

        // Act
        var result = await ExecutePipelineAsync("Test query", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.AiServiceFailed);
        result.Fallback!.Reason.Should().Be(FallbackReason.InvalidAiResponse);
    }

    [Fact]
    public async Task Pipeline_AiServiceReturnsEmptySql_ReturnsFallback()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId, ""); // Empty SQL

        // Act
        var result = await ExecutePipelineAsync("Unclear question", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ValidationFailed);
    }

    #endregion

    #region Database Failure Scenarios

    [Fact]
    public async Task Pipeline_QueryReferencesNonExistentTable_ReturnsFallback()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var sql = "SELECT * FROM non_existent_table";

        SetupAiServiceResponse(requestId, correlationId, sql);
        SetupFailedQueryExecution(sql, new InvalidOperationException("relation \"non_existent_table\" does not exist"));

        // Act
        var result = await ExecutePipelineAsync("Show fake data", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ExecutionFailed);
        result.Fallback!.Reason.Should().Be(FallbackReason.DatabaseExecutionFailed);
    }

    [Fact]
    public async Task Pipeline_DatabaseTimeout_ReturnsFallback()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var sql = "SELECT * FROM orders";

        SetupAiServiceResponse(requestId, correlationId, sql);
        SetupFailedQueryExecution(sql, new TimeoutException("Query execution timed out"));

        // Act
        var result = await ExecutePipelineAsync("Show all orders", correlationId, requestId);

        // Assert
        result.Status.Should().Be(PipelineStatus.ExecutionFailed);
        result.Fallback.Should().NotBeNull();
    }

    #endregion

    #region Audit Trail Verification

    [Fact]
    public async Task Pipeline_SuccessfulQuery_GeneratesCompleteAuditTrail()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var naturalLanguageQuery = "Tel alle orders";
        var sql = "SELECT COUNT(*) FROM orders";

        SetupAiServiceResponse(requestId, correlationId, sql);
        SetupSuccessfulQueryExecution(sql, new[] { new Dictionary<string, object?> { ["count"] = 6L } });

        // Act
        var result = await ExecutePipelineAsync(naturalLanguageQuery, correlationId, requestId);
        var auditEntry = result.AuditEntry;

        // Assert
        auditEntry.Should().NotBeNull();
        auditEntry!.CorrelationId.Should().Be(correlationId);
        auditEntry.RequestId.Should().Be(requestId);
        auditEntry.UserQuestion.Should().Be(naturalLanguageQuery);
        auditEntry.GeneratedSql.Should().Be(sql);
        auditEntry.ValidationResult.Should().Be(ValidationOutcome.Passed);
        auditEntry.ExecutionStatus.Should().Be(ExecutionOutcome.Success);
        auditEntry.TotalDurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Pipeline_BlockedQuery_GeneratesAuditWithBlockedStatus()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        SetupAiServiceResponse(requestId, correlationId, "DELETE FROM orders");

        // Act
        var result = await ExecutePipelineAsync("Delete data", correlationId, requestId);
        var auditEntry = result.AuditEntry;

        // Assert
        auditEntry.Should().NotBeNull();
        auditEntry!.ValidationResult.Should().BeOneOf(
            ValidationOutcome.Failed,
            ValidationOutcome.UnsafeBlocked);
        auditEntry.ExecutionStatus.Should().Be(ExecutionOutcome.ValidationBlocked);
    }

    #endregion

    #region Pipeline Helpers

    private void SetupAiServiceResponse(string requestId, string correlationId, string sql)
    {
        SetupAiServiceResponseWithConfidence(requestId, correlationId, sql, 0.92);
    }

    private void SetupAiServiceResponseWithConfidence(
        string requestId,
        string correlationId,
        string sql,
        double confidenceScore)
    {
        var response = new
        {
            requestId,
            correlationId,
            sqlProposal = new
            {
                dialect = "postgresql",
                sql,
                parameters = Array.Empty<object>()
            },
            explanationMetadata = new
            {
                intentSummary = "Generated query",
                reasoningSummary = "Based on user input",
                assumptions = new[] { "Default assumption" },
                selectedTables = new[] { "orders" },
                selectedColumns = new[] { "*" },
                confidenceScore,
                warnings = Array.Empty<string>()
            }
        };

        _aiServiceMock.Reset();
        _aiServiceMock
            .Given(Request.Create().WithPath("/generate-sql").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })));
    }

    private void SetupSuccessfulQueryExecution(string sql, IEnumerable<Dictionary<string, object?>> rows)
    {
        var rowList = rows.ToList();
        var result = new ReadOnlyQueryResult(
            Rows: rowList.Cast<IReadOnlyDictionary<string, object?>>().ToList(),
            RowCount: rowList.Count,
            DurationMs: 10,
            Truncated: false
        );

        _queryExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<DatabaseProfileId>(), sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private void SetupFailedQueryExecution(string sql, Exception exception)
    {
        _queryExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<DatabaseProfileId>(), sql, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    private async Task<PipelineResult> ExecutePipelineAsync(
        string naturalLanguageQuery,
        string correlationId,
        string requestId,
        TimeSpan? timeout = null)
    {
        var auditBuilder = new AnalyticsAuditEntryBuilder()
            .WithCorrelationId(correlationId)
            .WithRequestId(requestId)
            .WithUserQuestion(naturalLanguageQuery, "nl-NL");

        try
        {
            // Step 1: Call AI Service
            var aiRequest = new GenerateSqlRequest
            {
                RequestId = requestId,
                CorrelationId = correlationId,
                NaturalLanguageQuery = naturalLanguageQuery,
                Locale = "nl-NL",
                SchemaMetadata = CreateSchemaMetadata()
            };

            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
            var aiResult = await _aiServiceClient.GenerateSqlAsync(aiRequest, cts.Token);

            if (!aiResult.IsSuccess)
            {
                var fallback = _fallbackHandler.HandleAiServiceFailure(
                    aiResult.FailureType!.Value,
                    aiResult.FailureMessage ?? "Unknown error",
                    naturalLanguageQuery);

                auditBuilder.WithAiServiceFailure(aiResult.FailureType.Value, aiResult.FailureMessage ?? "");

                return new PipelineResult
                {
                    Status = PipelineStatus.AiServiceFailed,
                    Fallback = fallback,
                    AuditEntry = auditBuilder.Build()
                };
            }

            var sql = aiResult.Response!.SqlProposal.Sql;
            var confidence = aiResult.Response.ExplanationMetadata.ConfidenceScore;

            auditBuilder.WithAiResponse(
                sql,
                confidence,
                aiResult.Response.ExplanationMetadata.SelectedTables,
                aiResult.Response.ExplanationMetadata.SelectedColumns,
                0);

            // Step 2: Validate SQL
            var validation = _sqlValidator.Validate(sql);
            auditBuilder.WithValidationResult(validation, 0);

            if (!validation.IsValid || validation.SuspiciousPatterns.Count > 0)
            {
                var fallback = _fallbackHandler.HandleValidationFailure(
                    validation, sql, naturalLanguageQuery);

                auditBuilder.WithValidationBlocked();

                return new PipelineResult
                {
                    Status = PipelineStatus.ValidationFailed,
                    Fallback = fallback,
                    AuditEntry = auditBuilder.Build()
                };
            }

            // Step 3: Execute Query (using mock)
            try
            {
                var queryResult = await _queryExecutorMock.Object.ExecuteAsync(
                    DatabaseProfileId.AnalyticsReadOnly, sql, cts.Token);

                auditBuilder.WithExecutionSuccess(queryResult.DurationMs, queryResult.Truncated);

                return new PipelineResult
                {
                    Status = PipelineStatus.Success,
                    Data = queryResult,
                    Metadata = new ResultMetadata
                    {
                        ConfidenceScore = confidence,
                        GeneratedSql = sql
                    },
                    AuditEntry = auditBuilder.Build()
                };
            }
            catch (Exception dbEx)
            {
                var fallback = _fallbackHandler.HandleDatabaseFailure(
                    dbEx, sql, naturalLanguageQuery);

                auditBuilder.WithExecutionFailure(
                    ExecutionOutcome.QueryError, dbEx.Message, dbEx.GetType().Name);

                return new PipelineResult
                {
                    Status = PipelineStatus.ExecutionFailed,
                    Fallback = fallback,
                    AuditEntry = auditBuilder.Build()
                };
            }
        }
        catch (OperationCanceledException)
        {
            var fallback = _fallbackHandler.HandleAiServiceFailure(
                AiServiceFailureType.Timeout,
                "Request timed out",
                naturalLanguageQuery);

            auditBuilder.WithAiServiceFailure(AiServiceFailureType.Timeout, "Timeout");

            return new PipelineResult
            {
                Status = PipelineStatus.AiServiceFailed,
                Fallback = fallback,
                AuditEntry = auditBuilder.Build()
            };
        }
    }

    private static SchemaMetadata CreateSchemaMetadata() => new()
    {
        Dialect = "postgresql",
        Tables = new List<TableMetadata>
        {
            new()
            {
                TableName = "orders",
                Description = "Customer orders",
                Columns = new List<ColumnMetadata>
                {
                    new() { ColumnName = "id", DataType = "integer" },
                    new() { ColumnName = "customer_id", DataType = "integer" },
                    new() { ColumnName = "customer_name", DataType = "varchar" },
                    new() { ColumnName = "status", DataType = "varchar" },
                    new() { ColumnName = "total_amount", DataType = "decimal" },
                    new() { ColumnName = "created_at", DataType = "timestamp" }
                }
            },
            new()
            {
                TableName = "customers",
                Description = "Customer information",
                Columns = new List<ColumnMetadata>
                {
                    new() { ColumnName = "id", DataType = "integer" },
                    new() { ColumnName = "name", DataType = "varchar" },
                    new() { ColumnName = "email", DataType = "varchar" },
                    new() { ColumnName = "country", DataType = "varchar" }
                }
            },
            new()
            {
                TableName = "products",
                Description = "Product catalog",
                Columns = new List<ColumnMetadata>
                {
                    new() { ColumnName = "id", DataType = "integer" },
                    new() { ColumnName = "name", DataType = "varchar" },
                    new() { ColumnName = "price", DataType = "decimal" },
                    new() { ColumnName = "stock", DataType = "integer" }
                }
            }
        }
    };

    #endregion
}

#region Result Types

public enum PipelineStatus
{
    Success,
    AiServiceFailed,
    ValidationFailed,
    ExecutionFailed
}

public class PipelineResult
{
    public PipelineStatus Status { get; init; }
    public ReadOnlyQueryResult? Data { get; init; }
    public FallbackInfo? Fallback { get; init; }
    public ResultMetadata Metadata { get; init; } = new();
    public AnalyticsAuditEntry? AuditEntry { get; init; }
}

public class ResultMetadata
{
    public double ConfidenceScore { get; init; }
    public string GeneratedSql { get; init; } = string.Empty;
}

#endregion

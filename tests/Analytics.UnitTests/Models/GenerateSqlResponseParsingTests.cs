using System.Text.Json;
using Analytics.Application.Models;
using FluentAssertions;
using Xunit;

namespace Analytics.UnitTests.Models;

public class GenerateSqlResponseParsingTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Deserialize_ValidFullResponse_ParsesAllFields()
    {
        var json = """
        {
            "requestId": "req-123",
            "correlationId": "corr-456",
            "sqlProposal": {
                "dialect": "postgresql",
                "sql": "SELECT COUNT(*) FROM orders WHERE status = @status",
                "parameters": [
                    { "name": "status", "value": "active" }
                ]
            },
            "explanationMetadata": {
                "intentSummary": "Count active orders",
                "reasoningSummary": "User wants to know how many orders are active",
                "selectedTables": ["orders"],
                "selectedColumns": ["status"],
                "assumptions": ["Status field indicates order state"],
                "confidenceScore": 0.92,
                "warnings": []
            }
        }
        """;

        var result = JsonSerializer.Deserialize<GenerateSqlResponse>(json, _jsonOptions);

        result.Should().NotBeNull();
        result!.RequestId.Should().Be("req-123");
        result.CorrelationId.Should().Be("corr-456");
        result.SqlProposal.Dialect.Should().Be("postgresql");
        result.SqlProposal.Sql.Should().Contain("SELECT COUNT(*)");
        result.SqlProposal.Parameters.Should().HaveCount(1);
        result.SqlProposal.Parameters[0].Name.Should().Be("status");
        result.SqlProposal.Parameters[0].Value?.ToString().Should().Be("active");
        result.ExplanationMetadata.ConfidenceScore.Should().Be(0.92);
        result.ExplanationMetadata.SelectedTables.Should().Contain("orders");
    }

    [Fact]
    public void Deserialize_EmptyParameters_ParsesSuccessfully()
    {
        var json = """
        {
            "requestId": "req-123",
            "correlationId": "corr-456",
            "sqlProposal": {
                "dialect": "postgresql",
                "sql": "SELECT * FROM users",
                "parameters": []
            },
            "explanationMetadata": {
                "intentSummary": "Get all users",
                "reasoningSummary": "Fetch complete user list",
                "selectedTables": ["users"],
                "selectedColumns": ["*"],
                "assumptions": [],
                "confidenceScore": 0.95,
                "warnings": []
            }
        }
        """;

        var result = JsonSerializer.Deserialize<GenerateSqlResponse>(json, _jsonOptions);

        result.Should().NotBeNull();
        result!.SqlProposal.Parameters.Should().BeEmpty();
        result.ExplanationMetadata.Assumptions.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_MultipleParameters_ParsesAllParameters()
    {
        var json = """
        {
            "requestId": "req-123",
            "correlationId": "corr-456",
            "sqlProposal": {
                "dialect": "postgresql",
                "sql": "SELECT * FROM orders WHERE date >= @start AND date <= @end",
                "parameters": [
                    { "name": "start", "value": "2026-01-01" },
                    { "name": "end", "value": "2026-03-07" }
                ]
            },
            "explanationMetadata": {
                "intentSummary": "Get orders in date range",
                "reasoningSummary": "Filter orders by date",
                "selectedTables": ["orders"],
                "selectedColumns": ["date"],
                "assumptions": ["Date format is YYYY-MM-DD"],
                "confidenceScore": 0.88,
                "warnings": ["Large date range may return many results"]
            }
        }
        """;

        var result = JsonSerializer.Deserialize<GenerateSqlResponse>(json, _jsonOptions);

        result.Should().NotBeNull();
        result!.SqlProposal.Parameters.Should().HaveCount(2);
        result.ExplanationMetadata.Warnings.Should().HaveCount(1);
    }

    [Fact]
    public void Deserialize_LowConfidenceScore_ParsesCorrectly()
    {
        var json = """
        {
            "requestId": "req-low",
            "correlationId": "corr-low",
            "sqlProposal": {
                "dialect": "postgresql",
                "sql": "SELECT * FROM ambiguous_table",
                "parameters": []
            },
            "explanationMetadata": {
                "intentSummary": "Unclear intent",
                "reasoningSummary": "Could not determine exact requirement",
                "selectedTables": ["ambiguous_table"],
                "selectedColumns": ["*"],
                "assumptions": ["Guessed table name", "No clear filter"],
                "confidenceScore": 0.35,
                "warnings": ["Low confidence - please verify"]
            }
        }
        """;

        var result = JsonSerializer.Deserialize<GenerateSqlResponse>(json, _jsonOptions);

        result.Should().NotBeNull();
        result!.ExplanationMetadata.ConfidenceScore.Should().Be(0.35);
        result.ExplanationMetadata.Assumptions.Should().HaveCount(2);
        result.ExplanationMetadata.Warnings.Should().Contain("Low confidence - please verify");
    }

    [Fact]
    public void Deserialize_WithNumericParameterValue_ParsesAsNumber()
    {
        var json = """
        {
            "requestId": "req-num",
            "correlationId": "corr-num",
            "sqlProposal": {
                "dialect": "postgresql",
                "sql": "SELECT * FROM orders WHERE amount > @minAmount",
                "parameters": [
                    { "name": "minAmount", "value": 100.50 }
                ]
            },
            "explanationMetadata": {
                "intentSummary": "Orders above threshold",
                "reasoningSummary": "Filter by amount",
                "selectedTables": ["orders"],
                "selectedColumns": ["amount"],
                "assumptions": [],
                "confidenceScore": 0.9,
                "warnings": []
            }
        }
        """;

        var result = JsonSerializer.Deserialize<GenerateSqlResponse>(json, _jsonOptions);

        result.Should().NotBeNull();
        var paramValue = result!.SqlProposal.Parameters[0].Value;
        paramValue.Should().NotBeNull();
    }

    [Fact]
    public void Serialize_ValidResponse_ProducesCorrectJson()
    {
        var response = new GenerateSqlResponse
        {
            RequestId = "req-serialize",
            CorrelationId = "corr-serialize",
            SqlProposal = new SqlProposal
            {
                Dialect = "postgresql",
                Sql = "SELECT 1",
                Parameters = new List<SqlParameter>()
            },
            ExplanationMetadata = new ExplanationMetadata
            {
                IntentSummary = "Test",
                ReasoningSummary = "Test reasoning",
                SelectedTables = new List<string> { "test_table" },
                SelectedColumns = new List<string> { "col1" },
                Assumptions = new List<string>(),
                ConfidenceScore = 1.0,
                Warnings = new List<string>()
            }
        };

        var json = JsonSerializer.Serialize(response, _jsonOptions);

        json.Should().Contain("\"requestId\"");
        json.Should().Contain("\"sqlProposal\"");
        json.Should().Contain("\"confidenceScore\"");
    }

    [Fact]
    public void Deserialize_MissingRequiredField_ThrowsJsonException()
    {
        var json = """
        {
            "requestId": "req-123"
        }
        """;

        var act = () => JsonSerializer.Deserialize<GenerateSqlResponse>(json, _jsonOptions);

        act.Should().Throw<JsonException>();
    }
}

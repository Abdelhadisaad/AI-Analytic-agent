using System.Text.Json.Serialization;

namespace Analytics.Application.Models;

/// <summary>
/// Response from the AI service containing generated SQL.
/// </summary>
public sealed class GenerateSqlResponse
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("sqlProposal")]
    public required SqlProposal SqlProposal { get; init; }

    [JsonPropertyName("explanationMetadata")]
    public required ExplanationMetadata ExplanationMetadata { get; init; }
}

/// <summary>
/// The generated SQL proposal.
/// </summary>
public sealed class SqlProposal
{
    [JsonPropertyName("dialect")]
    public required string Dialect { get; init; }

    [JsonPropertyName("sql")]
    public required string Sql { get; init; }

    [JsonPropertyName("parameters")]
    public required IReadOnlyList<SqlParameter> Parameters { get; init; }
}

/// <summary>
/// A named SQL parameter.
/// </summary>
public sealed class SqlParameter
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("value")]
    public object? Value { get; init; }
}

/// <summary>
/// Metadata explaining the AI's reasoning and assumptions.
/// </summary>
public sealed class ExplanationMetadata
{
    [JsonPropertyName("intentSummary")]
    public required string IntentSummary { get; init; }

    [JsonPropertyName("reasoningSummary")]
    public required string ReasoningSummary { get; init; }

    [JsonPropertyName("selectedTables")]
    public required IReadOnlyList<string> SelectedTables { get; init; }

    [JsonPropertyName("selectedColumns")]
    public required IReadOnlyList<string> SelectedColumns { get; init; }

    [JsonPropertyName("assumptions")]
    public required IReadOnlyList<string> Assumptions { get; init; }

    [JsonPropertyName("confidenceScore")]
    public required double ConfidenceScore { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }
}

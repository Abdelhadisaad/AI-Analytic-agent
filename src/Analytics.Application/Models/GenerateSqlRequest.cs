using System.Text.Json.Serialization;

namespace Analytics.Application.Models;

/// <summary>
/// Request to generate SQL from natural language query.
/// </summary>
public sealed class GenerateSqlRequest
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("naturalLanguageQuery")]
    public required string NaturalLanguageQuery { get; init; }

    [JsonPropertyName("locale")]
    public string? Locale { get; init; }

    [JsonPropertyName("schemaMetadata")]
    public required SchemaMetadata SchemaMetadata { get; init; }
}

/// <summary>
/// Database schema metadata provided to the AI service.
/// </summary>
public sealed class SchemaMetadata
{
    [JsonPropertyName("dialect")]
    public required string Dialect { get; init; }

    [JsonPropertyName("tables")]
    public required IReadOnlyList<TableMetadata> Tables { get; init; }
}

/// <summary>
/// Metadata describing a single database table.
/// </summary>
public sealed class TableMetadata
{
    [JsonPropertyName("tableName")]
    public required string TableName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("columns")]
    public required IReadOnlyList<ColumnMetadata> Columns { get; init; }
}

/// <summary>
/// Metadata describing a single column in a table.
/// </summary>
public sealed class ColumnMetadata
{
    [JsonPropertyName("columnName")]
    public required string ColumnName { get; init; }

    [JsonPropertyName("dataType")]
    public required string DataType { get; init; }

    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// Discovers PostgreSQL schema metadata by querying information_schema.
/// Caches results per profile to avoid repeated database calls within the same request lifetime.
/// </summary>
public sealed class NpgsqlSchemaDiscoveryService : ISchemaDiscoveryService
{
    private const string DiscoverySql = @"
        SELECT
            t.table_name,
            c.column_name,
            c.data_type,
            CASE WHEN c.is_nullable = 'YES' THEN true ELSE false END AS is_nullable,
            pgd.description AS column_comment
        FROM information_schema.tables t
        JOIN information_schema.columns c
            ON c.table_schema = t.table_schema AND c.table_name = t.table_name
        LEFT JOIN pg_catalog.pg_statio_all_tables psat
            ON psat.schemaname = t.table_schema AND psat.relname = t.table_name
        LEFT JOIN pg_catalog.pg_description pgd
            ON pgd.objoid = psat.relid AND pgd.objsubid = c.ordinal_position
        WHERE t.table_schema = 'public'
          AND t.table_type = 'BASE TABLE'
        ORDER BY t.table_name, c.ordinal_position;";

    private readonly IDatabaseProfileResolver _profileResolver;
    private readonly ILogger<NpgsqlSchemaDiscoveryService> _logger;

    public NpgsqlSchemaDiscoveryService(
        IDatabaseProfileResolver profileResolver,
        ILogger<NpgsqlSchemaDiscoveryService> logger)
    {
        _profileResolver = profileResolver;
        _logger = logger;
    }

    public async Task<SchemaMetadata> DiscoverAsync(DatabaseProfileId profileId, CancellationToken ct = default)
    {
        var profile = _profileResolver.Resolve(profileId);

        _logger.LogInformation(
            "Discovering schema for profile {ProfileId} from database",
            profileId);

        await using var connection = new NpgsqlConnection(profile.ConnectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(DiscoverySql, connection)
        {
            CommandTimeout = 10
        };

        await using var reader = await command.ExecuteReaderAsync(ct);

        var tableMap = new Dictionary<string, List<ColumnMetadata>>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(ct))
        {
            var tableName = reader.GetString(0);
            var columnName = reader.GetString(1);
            var dataType = reader.GetString(2);
            var isNullable = reader.GetBoolean(3);
            var comment = reader.IsDBNull(4) ? null : reader.GetString(4);

            if (!tableMap.ContainsKey(tableName))
                tableMap[tableName] = new List<ColumnMetadata>();

            tableMap[tableName].Add(new ColumnMetadata
            {
                ColumnName = columnName,
                DataType = dataType,
                IsNullable = isNullable,
                Description = comment
            });
        }

        var tables = tableMap.Select(kvp => new TableMetadata
        {
            TableName = kvp.Key,
            Columns = kvp.Value
        }).ToList();

        _logger.LogInformation(
            "Discovered {TableCount} tables with {ColumnCount} total columns for profile {ProfileId}",
            tables.Count,
            tables.Sum(t => t.Columns.Count),
            profileId);

        return new SchemaMetadata
        {
            Dialect = "postgresql",
            Tables = tables
        };
    }
}

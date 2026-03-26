using System.Data;
using System.Diagnostics;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Npgsql;

namespace Analytics.Infrastructure.Persistence.Postgres;

public sealed class NpgsqlReadOnlyQueryExecutor : IReadOnlyQueryExecutor
{
    private readonly IDatabaseProfileResolver _profileResolver;
    private readonly ISqlValidator _sqlValidator;

    public NpgsqlReadOnlyQueryExecutor(IDatabaseProfileResolver profileResolver, ISqlValidator sqlValidator)
    {
        _profileResolver = profileResolver;
        _sqlValidator = sqlValidator;
    }

    public async Task<ReadOnlyQueryResult> ExecuteAsync(DatabaseProfileId profileId, string sql, CancellationToken ct)
    {
        var validation = _sqlValidator.Validate(sql);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"SQL rejected: {string.Join(" | ", validation.Errors)}");
        }

        if (validation.SuspiciousPatterns.Count > 0)
        {
            throw new InvalidOperationException(
                $"SQL rejected due to suspicious patterns: {string.Join(", ", validation.SuspiciousPatterns)}");
        }

        var profile = _profileResolver.Resolve(profileId);
        if (!profile.IsReadOnly)
        {
            throw new InvalidOperationException("Resolved profile is not read-only.");
        }

        await using var connection = new NpgsqlConnection(profile.ConnectionString);
        await connection.OpenAsync(ct);

        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        await using (var readOnlyTransactionCommand = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
        {
            await readOnlyTransactionCommand.ExecuteNonQueryAsync(ct);
        }

        await using (var statementTimeoutCommand = new NpgsqlCommand(
                         $"SET LOCAL statement_timeout = {profile.CommandTimeoutSeconds * 1000}", connection, transaction))
        {
            await statementTimeoutCommand.ExecuteNonQueryAsync(ct);
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var stopwatch = Stopwatch.StartNew();

        await using var queryCommand = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandType = CommandType.Text,
            CommandTimeout = profile.CommandTimeoutSeconds
        };

        await using var reader = await queryCommand.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= profile.MaxRows)
            {
                break;
            }

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(ct);
        stopwatch.Stop();

        var truncated = rows.Count >= profile.MaxRows;
        return new ReadOnlyQueryResult(rows, rows.Count, stopwatch.ElapsedMilliseconds, truncated);
    }
}

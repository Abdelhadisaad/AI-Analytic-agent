using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Logging;

namespace Analytics.Application.UseCases.ExecuteAnalyticsQuery;

/// <summary>
/// Orchestrates the full analytics query pipeline:
/// schema discovery → AI SQL generation → SQL validation → read-only execution → fallback handling.
/// 
/// This use-case is the single place where business flow is controlled.
/// The UI layer only calls this and maps the result to presentation.
/// </summary>
public sealed class ExecuteAnalyticsQueryUseCase
{
    private readonly IAiServiceClient _aiServiceClient;
    private readonly ISqlValidator _sqlValidator;
    private readonly IReadOnlyQueryExecutor _queryExecutor;
    private readonly IFallbackHandler _fallbackHandler;
    private readonly ISchemaDiscoveryService _schemaDiscovery;
    private readonly ILogger<ExecuteAnalyticsQueryUseCase> _logger;

    public ExecuteAnalyticsQueryUseCase(
        IAiServiceClient aiServiceClient,
        ISqlValidator sqlValidator,
        IReadOnlyQueryExecutor queryExecutor,
        IFallbackHandler fallbackHandler,
        ISchemaDiscoveryService schemaDiscovery,
        ILogger<ExecuteAnalyticsQueryUseCase> logger)
    {
        _aiServiceClient = aiServiceClient;
        _sqlValidator = sqlValidator;
        _queryExecutor = queryExecutor;
        _fallbackHandler = fallbackHandler;
        _schemaDiscovery = schemaDiscovery;
        _logger = logger;
    }

    /// <summary>
    /// Executes the full analytics pipeline for a natural language query.
    /// </summary>
    public async Task<AnalyticsQueryResult> ExecuteAsync(AnalyticsQueryInput input, CancellationToken ct = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "Pipeline gestart – CorrelationId={CorrelationId}, Profiel={Profile}, Vraag={Query}",
            correlationId, input.ProfileId, input.NaturalLanguageQuery);

        // ── 1. Schema ophalen ────────────────────────────────────
        var schemaMetadata = await _schemaDiscovery.DiscoverAsync(input.ProfileId, ct);

        // ── 2. AI SQL genereren ──────────────────────────────────
        var request = new GenerateSqlRequest
        {
            RequestId = requestId,
            CorrelationId = correlationId,
            NaturalLanguageQuery = input.NaturalLanguageQuery,
            Locale = input.Locale,
            SchemaMetadata = schemaMetadata
        };

        var aiResult = await _aiServiceClient.GenerateSqlAsync(request, ct);

        if (!aiResult.IsSuccess)
        {
            _logger.LogWarning(
                "AI service mislukt – CorrelationId={CorrelationId}, Type={FailureType}",
                correlationId, aiResult.FailureType);

            var fallback = _fallbackHandler.HandleAiServiceFailure(
                aiResult.FailureType!.Value,
                aiResult.FailureMessage ?? "Onbekende fout",
                input.NaturalLanguageQuery);

            return AnalyticsQueryResult.WithFallback(fallback, correlationId, requestId);
        }

        var sqlProposal = aiResult.Response!.SqlProposal;
        var explanation = aiResult.Response.ExplanationMetadata;

        // ── 3. SQL valideren ─────────────────────────────────────
        var validationResult = _sqlValidator.Validate(sqlProposal.Sql);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "SQL validatie gefaald – CorrelationId={CorrelationId}, Fouten={Errors}",
                correlationId, string.Join("; ", validationResult.Errors));

            var fallback = _fallbackHandler.HandleValidationFailure(
                validationResult, sqlProposal.Sql, input.NaturalLanguageQuery);

            return AnalyticsQueryResult.Rejected(fallback, correlationId, requestId);
        }

        // ── 4. Query uitvoeren ───────────────────────────────────
        try
        {
            var queryResult = await _queryExecutor.ExecuteAsync(input.ProfileId, sqlProposal.Sql, ct);

            _logger.LogInformation(
                "Pipeline voltooid – CorrelationId={CorrelationId}, Rijen={RowCount}, Duur={DurationMs}ms",
                correlationId, queryResult.RowCount, queryResult.DurationMs);

            var data = new QueryResultData
            {
                ExecutedSql = sqlProposal.Sql,
                Rows = queryResult.Rows,
                TotalRowCount = queryResult.RowCount,
                IsTruncated = queryResult.Truncated,
                ExecutionDurationMs = queryResult.DurationMs,
                Explanation = explanation
            };

            return AnalyticsQueryResult.Success(data, correlationId, requestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Database-uitvoering mislukt – CorrelationId={CorrelationId}", correlationId);

            var fallback = _fallbackHandler.HandleDatabaseFailure(
                ex, sqlProposal.Sql, input.NaturalLanguageQuery);

            return AnalyticsQueryResult.WithFallback(fallback, correlationId, requestId);
        }
    }
}

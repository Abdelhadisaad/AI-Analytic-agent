using System.ComponentModel.DataAnnotations;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Analytics.Api.Pages;

public class IndexModel : PageModel
{
    private readonly IAiServiceClient _aiServiceClient;
    private readonly ISqlValidator _sqlValidator;
    private readonly IReadOnlyQueryExecutor _queryExecutor;
    private readonly IFallbackHandler _fallbackHandler;
    private readonly ISchemaDiscoveryService _schemaDiscovery;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IAiServiceClient aiServiceClient,
        ISqlValidator sqlValidator,
        IReadOnlyQueryExecutor queryExecutor,
        IFallbackHandler fallbackHandler,
        ISchemaDiscoveryService schemaDiscovery,
        ILogger<IndexModel> logger)
    {
        _aiServiceClient = aiServiceClient;
        _sqlValidator = sqlValidator;
        _queryExecutor = queryExecutor;
        _fallbackHandler = fallbackHandler;
        _schemaDiscovery = schemaDiscovery;
        _logger = logger;
    }


    [BindProperty]
    [Required(ErrorMessage = "Voer een vraag in.")]
    public string NaturalLanguageQuery { get; set; } = string.Empty;

    [BindProperty]
    public string SelectedProfile { get; set; } = nameof(DatabaseProfileId.AnalyticsReadOnly);

    // ── Output properties ────────────────────────────────────────

    public bool HasResult { get; private set; }
    public string? GeneratedSql { get; private set; }
    public string? IntentSummary { get; private set; }
    public double? ConfidenceScore { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? SuggestedAction { get; private set; }
    public IReadOnlyList<string>? ValidationErrors { get; private set; }
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? ResultRows { get; private set; }
    public IReadOnlyList<string> ResultColumns { get; private set; } = Array.Empty<string>();
    public long? ExecutionDurationMs { get; private set; }
    public bool IsTruncated { get; private set; }

    public string ConfidenceCssClass => ConfidenceScore switch
    {
        >= 0.8 => "badge-success",
        >= 0.5 => "badge-warning",
        _ => "badge-danger"
    };


    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        HasResult = true;

        if (!ModelState.IsValid)
            return Page();

        if (!Enum.TryParse<DatabaseProfileId>(SelectedProfile, out var profileId))
        {
            ErrorMessage = "Ongeldig database profiel geselecteerd.";
            return Page();
        }

        var requestId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "Pipeline gestart – CorrelationId={CorrelationId}, Profiel={Profile}, Vraag={Query}",
            correlationId, profileId, NaturalLanguageQuery);

        // ── 1. Genereer SQL via AI service ───────────────────────
        var schemaMetadata = await _schemaDiscovery.DiscoverAsync(profileId, ct);

        var request = new GenerateSqlRequest
        {
            RequestId = requestId,
            CorrelationId = correlationId,
            NaturalLanguageQuery = NaturalLanguageQuery,
            Locale = "nl",
            SchemaMetadata = schemaMetadata
        };

        var aiResult = await _aiServiceClient.GenerateSqlAsync(request, ct);

        if (!aiResult.IsSuccess)
        {
            var fallback = _fallbackHandler.HandleAiServiceFailure(
                aiResult.FailureType!.Value, aiResult.FailureMessage ?? "Onbekende fout", NaturalLanguageQuery);
            SetFallback(fallback);
            return Page();
        }

        var sqlProposal = aiResult.Response!.SqlProposal;
        GeneratedSql = sqlProposal.Sql;
        IntentSummary = aiResult.Response.ExplanationMetadata.IntentSummary;
        ConfidenceScore = aiResult.Response.ExplanationMetadata.ConfidenceScore;

        // ── 2. Valideer de gegenereerde SQL ──────────────────────
        var validationResult = _sqlValidator.Validate(sqlProposal.Sql);

        if (!validationResult.IsValid)
        {
            var fallback = _fallbackHandler.HandleValidationFailure(
                validationResult, sqlProposal.Sql, NaturalLanguageQuery);
            SetFallback(fallback);
            return Page();
        }

        // ── 3. Voer de query uit ─────────────────────────────────
        try
        {
            var queryResult = await _queryExecutor.ExecuteAsync(profileId, sqlProposal.Sql, ct);

            ResultRows = queryResult.Rows;
            ExecutionDurationMs = queryResult.DurationMs;
            IsTruncated = queryResult.Truncated;

            if (queryResult.Rows.Count > 0)
            {
                ResultColumns = queryResult.Rows[0].Keys.ToList();
            }

            _logger.LogInformation(
                "Pipeline voltooid – CorrelationId={CorrelationId}, Rijen={RowCount}, Duur={DurationMs}ms",
                correlationId, queryResult.RowCount, queryResult.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database-uitvoering mislukt – CorrelationId={CorrelationId}", correlationId);
            var fallback = _fallbackHandler.HandleDatabaseFailure(ex, sqlProposal.Sql, NaturalLanguageQuery);
            SetFallback(fallback);
        }

        return Page();
    }

    // ── Private helpers ──────────────────────────────────────────

    private void SetFallback(FallbackInfo fallback)
    {
        ErrorMessage = fallback.Message;
        SuggestedAction = fallback.SuggestedAction;
        ValidationErrors = fallback.ValidationErrors;
    }


}

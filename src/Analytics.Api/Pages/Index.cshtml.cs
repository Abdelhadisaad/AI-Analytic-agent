using System.ComponentModel.DataAnnotations;
using Analytics.Application.Models;
using Analytics.Application.UseCases.ExecuteAnalyticsQuery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Analytics.Api.Pages;

/// <summary>
/// Razor Page that handles user input and displays results.
/// Contains NO business logic — delegates entirely to the use-case.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ExecuteAnalyticsQueryUseCase _useCase;

    public IndexModel(ExecuteAnalyticsQueryUseCase useCase)
    {
        _useCase = useCase;
    }

    // ── Input properties ─────────────────────────────────────────

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

    // ── Handlers ─────────────────────────────────────────────────

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

        // ── Delegate to use-case ─────────────────────────────────
        var input = new AnalyticsQueryInput(NaturalLanguageQuery, profileId);
        var result = await _useCase.ExecuteAsync(input, ct);

        // ── Map result to UI properties ──────────────────────────
        MapResultToView(result);

        return Page();
    }

    // ── Private helpers ──────────────────────────────────────────

    private void MapResultToView(AnalyticsQueryResult result)
    {
        switch (result.Status)
        {
            case AnalyticsQueryStatus.Success:
                var data = result.Data!;
                GeneratedSql = data.ExecutedSql;
                ResultRows = data.Rows;
                ExecutionDurationMs = data.ExecutionDurationMs;
                IsTruncated = data.IsTruncated;
                IntentSummary = data.Explanation?.IntentSummary;
                ConfidenceScore = data.Explanation?.ConfidenceScore;

                if (data.Rows.Count > 0)
                    ResultColumns = data.Rows[0].Keys.ToList();
                break;

            case AnalyticsQueryStatus.Fallback:
            case AnalyticsQueryStatus.Rejected:
                var fallback = result.Fallback!;
                ErrorMessage = fallback.Message;
                SuggestedAction = fallback.SuggestedAction;
                ValidationErrors = fallback.ValidationErrors;
                break;
        }
    }
}

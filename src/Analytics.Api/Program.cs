using Analytics.Application.Models;
using Analytics.Application.UseCases.ExecuteAnalyticsQuery;
using Analytics.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add Razor Pages
builder.Services.AddRazorPages();

// Register application use-cases
builder.Services.AddScoped<ExecuteAnalyticsQueryUseCase>();

// Register infrastructure services
builder.Services.AddAiServiceClient(builder.Configuration);
builder.Services.AddSqlValidation(builder.Configuration);
builder.Services.AddDatabaseProfileResolution(builder.Configuration);
builder.Services.AddReadOnlyQueryExecution();
builder.Services.AddSchemaDiscovery();
builder.Services.AddFallbackHandler();
builder.Services.AddAnalyticsAuditLogging();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// ── Evaluation endpoint (JSON) ───────────────────────────────────
// Used by the system tests / evaluation runner. Not exposed in production.
app.MapPost("/api/evaluate", async (EvaluateRequest req, ExecuteAnalyticsQueryUseCase useCase, CancellationToken ct) =>
{
    if (!Enum.TryParse<DatabaseProfileId>(req.Profile ?? "AnalyticsReadOnly", out var profileId))
        return Results.BadRequest("Invalid profile");

    var input = new AnalyticsQueryInput(req.Question, profileId, req.Locale ?? "nl");
    var result = await useCase.ExecuteAsync(input, ct);

    return Results.Ok(new EvaluateResponse
    {
        Status = result.Status.ToString(),
        Sql = result.Data?.ExecutedSql,
        RowCount = result.Data?.TotalRowCount,
        IsTruncated = result.Data?.IsTruncated ?? false,
        ExecutionDurationMs = result.Data?.ExecutionDurationMs,
        IntentSummary = result.Data?.Explanation?.IntentSummary,
        ConfidenceScore = result.Data?.Explanation?.ConfidenceScore,
        ErrorMessage = result.Fallback?.Message,
        SuggestedAction = result.Fallback?.SuggestedAction,
        ValidationErrors = result.Fallback?.ValidationErrors?.ToList(),
        FallbackReason = result.Fallback?.Reason.ToString(),
        IsRetryable = result.Fallback?.IsRetryable ?? false,
        CorrelationId = result.CorrelationId,
        RequestId = result.RequestId
    });
});

app.Run();

// ── Minimal DTOs for evaluation endpoint ─────────────────────────
public record EvaluateRequest(string Question, string? Profile = null, string? Locale = null);

public class EvaluateResponse
{
    public string Status { get; set; } = string.Empty;
    public string? Sql { get; set; }
    public int? RowCount { get; set; }
    public bool IsTruncated { get; set; }
    public long? ExecutionDurationMs { get; set; }
    public string? IntentSummary { get; set; }
    public double? ConfidenceScore { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuggestedAction { get; set; }
    public List<string>? ValidationErrors { get; set; }
    public string? FallbackReason { get; set; }
    public bool IsRetryable { get; set; }
    public string? CorrelationId { get; set; }
    public string? RequestId { get; set; }
}

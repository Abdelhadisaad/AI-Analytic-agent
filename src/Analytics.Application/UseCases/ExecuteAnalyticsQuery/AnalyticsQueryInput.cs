using Analytics.Application.Models;

namespace Analytics.Application.UseCases.ExecuteAnalyticsQuery;

/// <summary>
/// Input for the analytics query use-case.
/// Contains only what the UI needs to provide: the user's question and the selected database profile.
/// </summary>
public sealed record AnalyticsQueryInput(
    string NaturalLanguageQuery,
    DatabaseProfileId ProfileId,
    string Locale = "nl");

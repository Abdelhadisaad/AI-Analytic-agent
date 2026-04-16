using System.Text.Json;
using System.Text.Json.Serialization;
using Analytics.SystemTests.Models;

namespace Analytics.SystemTests.Reporting;

/// <summary>
/// Generates evaluation reports in JSON and human-readable console format.
/// </summary>
public static class EvaluationReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes the full evaluation report (results + summary) to a JSON file.
    /// </summary>
    public static async Task WriteJsonReportAsync(
        List<EvaluationResult> results,
        EvaluationSummary summary,
        string outputPath = "evaluation-report.json")
    {
        var report = new
        {
            summary,
            results
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json);
    }

    /// <summary>
    /// Prints a human-readable summary to the xUnit output.
    /// </summary>
    public static string GenerateConsoleSummary(EvaluationSummary summary)
    {
        var lines = new List<string>
        {
            "",
            "╔══════════════════════════════════════════════════════════════╗",
            "║               EVALUATION REPORT SUMMARY                    ║",
            "╠══════════════════════════════════════════════════════════════╣",
            $"║  Timestamp:           {summary.Timestamp:yyyy-MM-dd HH:mm:ss UTC}              ║",
            $"║  Total Queries:       {summary.TotalQueries,-38}║",
            "╠══════════════════════════════════════════════════════════════╣",
            "║  CORE METRICS                                              ║",
            "╠──────────────────────────────────────────────────────────────╣",
            $"║  SQL Validity Rate:   {summary.ValidityRate,6:F1}%  ({summary.ValidSqlCount}/{summary.TotalQueries}){Pad(summary.ValidSqlCount, summary.TotalQueries)}║",
            $"║  Execution Success:   {summary.ExecutionSuccessRate,6:F1}%  ({summary.ExecutionSuccessCount}/{summary.TotalQueries}){Pad(summary.ExecutionSuccessCount, summary.TotalQueries)}║",
            $"║  Rejection Rate:      {summary.RejectionRate,6:F1}%  ({summary.RejectedCount}/{summary.TotalQueries}){Pad(summary.RejectedCount, summary.TotalQueries)}║",
            $"║  Correctness Rate:    {summary.CorrectnessRate,6:F1}%  ({summary.CorrectnessPassedCount}/{summary.CorrectnessCheckedCount}){Pad(summary.CorrectnessPassedCount, summary.CorrectnessCheckedCount)}║",
            "╠──────────────────────────────────────────────────────────────╣",
            "║  LATENCY                                                    ║",
            "╠──────────────────────────────────────────────────────────────╣",
            $"║  Average:             {summary.AverageLatencyMs,8:F1} ms                        ║",
            $"║  Median:              {summary.MedianLatencyMs,8:F1} ms                        ║",
            $"║  P95:                 {summary.P95LatencyMs,8:F1} ms                        ║",
            $"║  Min:                 {summary.MinLatencyMs,8} ms                        ║",
            $"║  Max:                 {summary.MaxLatencyMs,8} ms                        ║"
        };

        // Per-category breakdown
        if (summary.ByCategory.Count > 0)
        {
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            lines.Add("║  BY CATEGORY                                                 ║");
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            foreach (var (cat, m) in summary.ByCategory.OrderBy(kv => kv.Key))
            {
                lines.Add($"║  {cat,-16} Valid:{m.ValidityRate,5:F1}%  Success:{m.SuccessRate,5:F1}%  Avg:{m.AverageLatencyMs,6:F0}ms ║");
            }
        }

        // Per-difficulty breakdown
        if (summary.ByDifficulty.Count > 0)
        {
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            lines.Add("║  BY DIFFICULTY                                               ║");
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            foreach (var (diff, m) in summary.ByDifficulty.OrderBy(kv => DifficultyOrder(kv.Key)))
            {
                lines.Add($"║  {diff,-16} Valid:{m.ValidityRate,5:F1}%  Success:{m.SuccessRate,5:F1}%  Avg:{m.AverageLatencyMs,6:F0}ms ║");
            }
        }

        // Failure analysis
        if (summary.FailuresByType.Count > 0)
        {
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            lines.Add("║  FAILURE ANALYSIS                                            ║");
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            foreach (var (type, count) in summary.FailuresByType.OrderByDescending(kv => kv.Value))
            {
                lines.Add($"║  {type,-30} {count,3} occurrences              ║");
            }
        }

        // Failed queries detail
        if (summary.FailedQueries.Count > 0)
        {
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            lines.Add("║  FAILED QUERIES                                              ║");
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            foreach (var fq in summary.FailedQueries.Take(15))
            {
                var questionSnippet = fq.Question.Length > 35
                    ? fq.Question[..35] + "..."
                    : fq.Question;
                lines.Add($"║  [{fq.QueryId}] {questionSnippet,-40} {fq.FailureType,-10}║");
            }
        }

        // Consistency
        if (summary.OverallConsistencyRate.HasValue)
        {
            lines.Add("╠──────────────────────────────────────────────────────────────╣");
            lines.Add($"║  CONSISTENCY RATE:    {summary.OverallConsistencyRate,6:F1}%                               ║");
        }

        lines.Add("╚══════════════════════════════════════════════════════════════╝");
        lines.Add("");

        return string.Join(Environment.NewLine, lines);
    }

    private static string Pad(int count, int total)
    {
        var text = $"({count}/{total})";
        // Ensure the line fills to the border
        var remaining = 22 - text.Length;
        return new string(' ', Math.Max(1, remaining));
    }

    private static int DifficultyOrder(string difficulty) => difficulty.ToLower() switch
    {
        "easy" => 0,
        "medium" => 1,
        "hard" => 2,
        _ => 3
    };
}

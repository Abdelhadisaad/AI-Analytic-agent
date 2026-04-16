using Analytics.SystemTests.Models;

namespace Analytics.SystemTests.Metrics;

/// <summary>
/// Analyzes failed queries and groups them by failure type.
/// Provides structured failure classification for thesis reporting.
///
/// Failure categories:
/// - InvalidSql:        AI generated SQL that failed validation
/// - ValidationRejected: SQL was blocked by the security validator (mutation, suspicious)
/// - ExecutionError:     SQL was valid but execution failed (non-existent table, syntax, timeout)
/// - EmptyResult:        Query succeeded but returned 0 rows (may indicate incorrect SQL)
/// - IncorrectResult:    SQL didn't contain expected keywords (semantic mismatch)
/// - AiServiceFailure:   AI service was unreachable, timed out, or returned invalid response
/// - Ambiguity:          Ambiguous queries that the system couldn't handle well
/// - Unknown:            Uncategorized failures
/// </summary>
public static class FailureAnalyzer
{
    public static Dictionary<string, int> ClassifyFailures(List<EvaluationResult> results)
    {
        var failures = new Dictionary<string, int>();

        foreach (var result in results)
        {
            var category = Classify(result);
            if (category != null)
            {
                failures[category] = failures.GetValueOrDefault(category, 0) + 1;
            }
        }

        return failures;
    }

    public static List<FailedQueryInfo> GetFailedQueries(List<EvaluationResult> results)
    {
        return results
            .Where(r => !r.ExecutionSucceeded || r.WasRejected || !string.IsNullOrEmpty(r.FailureType))
            .Select(r => new FailedQueryInfo
            {
                QueryId = r.QueryId,
                Question = r.Question,
                Category = r.Category,
                FailureType = Classify(r) ?? "Unknown",
                ErrorMessage = r.ErrorMessage
            })
            .ToList();
    }

    private static string? Classify(EvaluationResult result)
    {
        // Successful queries with results are not failures
        if (result.ExecutionSucceeded && !result.WasRejected)
        {
            // Check if result was empty (possible incorrect SQL)
            if (result.RowCount == 0)
                return "EmptyResult";

            // Check correctness (expected keyword not found in SQL)
            if (result.CorrectnessCheck == false)
                return "IncorrectResult";

            return null; // True success
        }

        // Rejected by validator
        if (result.WasRejected)
        {
            return result.FailureType switch
            {
                "UnsafeSqlDetected" => "ValidationRejected",
                "SuspiciousPatternsDetected" => "ValidationRejected",
                "SqlValidationFailed" => "InvalidSql",
                _ => "ValidationRejected"
            };
        }

        // AI service failures
        if (result.FailureType != null)
        {
            return result.FailureType switch
            {
                "AiServiceTimeout" or "Timeout" => "AiServiceFailure",
                "AiServiceUnavailable" or "ConnectionError" => "AiServiceFailure",
                "InvalidAiResponse" or "EmptyResponse" => "AiServiceFailure",
                "RetriesExhausted" => "AiServiceFailure",
                "DatabaseExecutionFailed" => "ExecutionError",
                "HttpError" => "AiServiceFailure",
                _ => "Unknown"
            };
        }

        return "Unknown";
    }
}

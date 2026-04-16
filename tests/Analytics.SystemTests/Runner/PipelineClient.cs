using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Analytics.SystemTests.Models;

namespace Analytics.SystemTests.Runner;

/// <summary>
/// HTTP client that calls the evaluation endpoint on the running Analytics.Api.
/// This is a real system test — it hits the actual API over HTTP.
///
/// Trade-off: using HTTP adds network overhead to latency measurements,
/// but this reflects real-world usage and tests the full stack including
/// serialization, routing, and middleware.
/// </summary>
public sealed class PipelineClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public PipelineClient(string baseUrl = "http://localhost:5200")
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Sends a natural language query through the full pipeline and returns the structured result.
    /// Measures end-to-end latency including HTTP overhead.
    /// </summary>
    public async Task<EvaluationResult> ExecuteQueryAsync(TestQuery query, CancellationToken ct = default)
    {
        var result = new EvaluationResult
        {
            QueryId = query.Id,
            Question = query.Question,
            Category = query.Category,
            Difficulty = query.Difficulty,
            ExpectedBehavior = query.ExpectedBehavior,
            ExpectedResultContains = query.ExpectedResultContains
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var request = new { question = query.Question, profile = "AnalyticsReadOnly", locale = "nl" };

            var response = await _httpClient.PostAsJsonAsync("/api/evaluate", request, _jsonOptions, ct);
            stopwatch.Stop();
            result.LatencyMs = stopwatch.ElapsedMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                result.FailureType = "HttpError";
                result.ErrorMessage = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                return result;
            }

            var body = await response.Content.ReadFromJsonAsync<PipelineResponse>(_jsonOptions, ct);

            if (body == null)
            {
                result.FailureType = "EmptyResponse";
                result.ErrorMessage = "API returned empty response body";
                return result;
            }

            // Map response fields
            result.GeneratedSql = body.Sql;
            result.ConfidenceScore = body.ConfidenceScore;
            result.IntentSummary = body.IntentSummary;
            result.RowCount = body.RowCount;

            switch (body.Status)
            {
                case "Success":
                    result.SqlIsValid = true;
                    result.ExecutionSucceeded = true;
                    break;

                case "Rejected":
                    result.WasRejected = true;
                    result.SqlIsValid = false;
                    result.FailureType = body.FallbackReason ?? "Rejected";
                    result.ErrorMessage = body.ErrorMessage;
                    result.ValidationErrors = body.ValidationErrors ?? new List<string>();
                    break;

                case "Fallback":
                    result.SqlIsValid = body.Sql != null;
                    result.FailureType = body.FallbackReason ?? "Fallback";
                    result.ErrorMessage = body.ErrorMessage;
                    break;
            }

            // Correctness check: does the SQL contain the expected keyword?
            if (!string.IsNullOrEmpty(query.ExpectedResultContains) && !string.IsNullOrEmpty(body.Sql))
            {
                result.CorrectnessCheck = body.Sql.Contains(query.ExpectedResultContains, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.FailureType = "Timeout";
            result.ErrorMessage = "Request timed out after 60 seconds";
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.FailureType = "ConnectionError";
            result.ErrorMessage = $"Cannot reach API: {ex.Message}";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.FailureType = "UnexpectedError";
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public void Dispose() => _httpClient.Dispose();

    /// <summary>
    /// Internal DTO matching the /api/evaluate response shape.
    /// </summary>
    private class PipelineResponse
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
}

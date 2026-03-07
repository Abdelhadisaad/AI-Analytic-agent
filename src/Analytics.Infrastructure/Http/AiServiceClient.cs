using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Analytics.Infrastructure.Http;

/// <summary>
/// Resilient HTTP client for communicating with the Python AI service.
/// Uses Polly for retry logic, timeout handling, and circuit breaker patterns.
/// </summary>
public sealed class AiServiceClient : IAiServiceClient
{
    private const string GenerateSqlEndpoint = "/generate-sql";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AiServiceClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public AiServiceClient(
        HttpClient httpClient,
        ILogger<AiServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc/>
    public async Task<AiServiceResult<GenerateSqlResponse>> GenerateSqlAsync(
        GenerateSqlRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = request.CorrelationId;

        _logger.LogInformation(
            "Sending SQL generation request to AI service. CorrelationId: {CorrelationId}, RequestId: {RequestId}",
            correlationId,
            request.RequestId);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                GenerateSqlEndpoint,
                request,
                _jsonOptions,
                cancellationToken);

            return await HandleResponseAsync(response, correlationId, cancellationToken);
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning(
                ex,
                "AI service request timed out. CorrelationId: {CorrelationId}",
                correlationId);

            return AiServiceResult<GenerateSqlResponse>.Failure(
                AiServiceFailureType.Timeout,
                "The AI service did not respond within the timeout period.");
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(
                ex,
                "AI service circuit breaker is open. CorrelationId: {CorrelationId}",
                correlationId);

            return AiServiceResult<GenerateSqlResponse>.Failure(
                AiServiceFailureType.RetriesExhausted,
                "The AI service is temporarily unavailable due to repeated failures.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Failed to connect to AI service. CorrelationId: {CorrelationId}",
                correlationId);

            return AiServiceResult<GenerateSqlResponse>.Failure(
                AiServiceFailureType.Unreachable,
                $"Could not reach the AI service: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(
                ex,
                "AI service request timed out (HttpClient timeout). CorrelationId: {CorrelationId}",
                correlationId);

            return AiServiceResult<GenerateSqlResponse>.Failure(
                AiServiceFailureType.Timeout,
                "The AI service request timed out.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "AI service request was cancelled. CorrelationId: {CorrelationId}",
                correlationId);

            throw; // Re-throw cancellation to propagate up
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error calling AI service. CorrelationId: {CorrelationId}",
                correlationId);

            return AiServiceResult<GenerateSqlResponse>.Failure(
                AiServiceFailureType.ServiceError,
                $"An unexpected error occurred: {ex.Message}");
        }
    }

    private async Task<AiServiceResult<GenerateSqlResponse>> HandleResponseAsync(
        HttpResponseMessage response,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var result = await response.Content.ReadFromJsonAsync<GenerateSqlResponse>(
                    _jsonOptions,
                    cancellationToken);

                if (result is null)
                {
                    _logger.LogWarning(
                        "AI service returned null response. CorrelationId: {CorrelationId}",
                        correlationId);

                    return AiServiceResult<GenerateSqlResponse>.Failure(
                        AiServiceFailureType.InvalidResponse,
                        "The AI service returned an empty response.");
                }

                _logger.LogInformation(
                    "Successfully received SQL generation response. CorrelationId: {CorrelationId}, Confidence: {Confidence}",
                    correlationId,
                    result.ExplanationMetadata.ConfidenceScore);

                return AiServiceResult<GenerateSqlResponse>.Success(result);
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to deserialize AI service response. CorrelationId: {CorrelationId}",
                    correlationId);

                return AiServiceResult<GenerateSqlResponse>.Failure(
                    AiServiceFailureType.InvalidResponse,
                    "Could not parse the AI service response.");
            }
        }

        // Handle error responses
        var errorBody = await TryReadErrorBodyAsync(response, cancellationToken);

        return response.StatusCode switch
        {
            HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout =>
                LogAndReturnFailure(
                    AiServiceFailureType.Timeout,
                    "AI service timed out (upstream timeout).",
                    correlationId,
                    response.StatusCode,
                    errorBody),

            HttpStatusCode.BadGateway =>
                LogAndReturnFailure(
                    AiServiceFailureType.ServiceError,
                    "AI service upstream error (LLM provider failure).",
                    correlationId,
                    response.StatusCode,
                    errorBody),

            HttpStatusCode.ServiceUnavailable =>
                LogAndReturnFailure(
                    AiServiceFailureType.ServiceError,
                    "AI service is temporarily unavailable.",
                    correlationId,
                    response.StatusCode,
                    errorBody),

            >= HttpStatusCode.InternalServerError =>
                LogAndReturnFailure(
                    AiServiceFailureType.ServiceError,
                    $"AI service internal error: {response.StatusCode}",
                    correlationId,
                    response.StatusCode,
                    errorBody),

            _ =>
                LogAndReturnFailure(
                    AiServiceFailureType.ServiceError,
                    $"AI service returned unexpected status: {response.StatusCode}",
                    correlationId,
                    response.StatusCode,
                    errorBody)
        };
    }

    private AiServiceResult<GenerateSqlResponse> LogAndReturnFailure(
        AiServiceFailureType failureType,
        string message,
        string correlationId,
        HttpStatusCode statusCode,
        string? errorBody)
    {
        _logger.LogWarning(
            "AI service error response. CorrelationId: {CorrelationId}, StatusCode: {StatusCode}, Message: {Message}, Body: {ErrorBody}",
            correlationId,
            statusCode,
            message,
            errorBody ?? "(empty)");

        return AiServiceResult<GenerateSqlResponse>.Failure(failureType, message);
    }

    private static async Task<string?> TryReadErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}

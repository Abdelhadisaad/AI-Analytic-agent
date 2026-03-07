namespace Analytics.Application.Models;

/// <summary>
/// Wraps the result of an AI service call, supporting graceful fallback handling.
/// </summary>
/// <typeparam name="T">The response type.</typeparam>
public sealed class AiServiceResult<T> where T : class
{
    /// <summary>
    /// True if the AI service call succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The response payload, if successful.
    /// </summary>
    public T? Response { get; }

    /// <summary>
    /// The failure type, if unsuccessful.
    /// </summary>
    public AiServiceFailureType? FailureType { get; }

    /// <summary>
    /// Human-readable failure message for logging/debugging.
    /// </summary>
    public string? FailureMessage { get; }

    private AiServiceResult(T response)
    {
        IsSuccess = true;
        Response = response;
        FailureType = null;
        FailureMessage = null;
    }

    private AiServiceResult(AiServiceFailureType failureType, string failureMessage)
    {
        IsSuccess = false;
        Response = null;
        FailureType = failureType;
        FailureMessage = failureMessage;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static AiServiceResult<T> Success(T response) => new(response);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static AiServiceResult<T> Failure(AiServiceFailureType failureType, string message) =>
        new(failureType, message);
}

/// <summary>
/// Classification of AI service failures for fallback logic.
/// </summary>
public enum AiServiceFailureType
{
    /// <summary>
    /// The AI service did not respond within the timeout period.
    /// </summary>
    Timeout,

    /// <summary>
    /// The AI service returned an error response (5xx).
    /// </summary>
    ServiceError,

    /// <summary>
    /// The AI service is unreachable (network/DNS failure).
    /// </summary>
    Unreachable,

    /// <summary>
    /// The response from the AI service could not be parsed.
    /// </summary>
    InvalidResponse,

    /// <summary>
    /// All retry attempts were exhausted.
    /// </summary>
    RetriesExhausted
}

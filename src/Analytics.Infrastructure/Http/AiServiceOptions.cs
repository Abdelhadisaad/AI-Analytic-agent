namespace Analytics.Infrastructure.Http;

/// <summary>
/// Configuration options for the AI service HTTP client.
/// </summary>
public sealed class AiServiceOptions
{
    public const string SectionName = "AiService";

    /// <summary>
    /// Base URL of the AI service (e.g., "http://localhost:8000").
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>
    /// Timeout for individual HTTP requests in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of retry attempts for transient failures.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Base delay between retries in seconds (exponential backoff multiplier).
    /// </summary>
    public int RetryBaseDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Circuit breaker: number of failures before opening the circuit.
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Circuit breaker: duration to keep the circuit open in seconds.
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
}

using System.Net;
using Analytics.Application.Abstractions;
using Analytics.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace Analytics.Infrastructure.DependencyInjection;

public static class AiServiceClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers the resilient AI service HTTP client with retry, timeout, and circuit breaker policies.
    /// </summary>
    public static IServiceCollection AddAiServiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AiServiceOptions>(configuration.GetSection(AiServiceOptions.SectionName));

        var options = configuration
            .GetSection(AiServiceOptions.SectionName)
            .Get<AiServiceOptions>() ?? new AiServiceOptions();

        services.AddHttpClient<IAiServiceClient, AiServiceClient>(client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds * (options.RetryCount + 1) + 10);
                client.DefaultRequestHeaders.Add("User-Agent", "Analytics-Orchestrator/1.0");
            })
            .AddPolicyHandler((serviceProvider, _) =>
                GetRetryPolicy(options, serviceProvider.GetRequiredService<ILoggerFactory>()))
            .AddPolicyHandler((serviceProvider, _) =>
                GetCircuitBreakerPolicy(options, serviceProvider.GetRequiredService<ILoggerFactory>()));

        return services;
    }

    /// <summary>
    /// Creates an exponential backoff retry policy for transient HTTP errors.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(
        AiServiceOptions options,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AiServiceClient.RetryPolicy");

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: options.RetryCount,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(options.RetryBaseDelaySeconds, retryAttempt)),
                onRetry: (outcome, timespan, retryAttempt, _) =>
                {
                    logger.LogWarning(
                        "AI service request failed. Retrying in {RetryDelay}s (attempt {RetryAttempt}/{MaxRetries}). " +
                        "StatusCode: {StatusCode}, Exception: {ExceptionMessage}",
                        timespan.TotalSeconds,
                        retryAttempt,
                        options.RetryCount,
                        outcome.Result?.StatusCode.ToString() ?? "N/A",
                        outcome.Exception?.Message ?? "N/A");
                });
    }

    /// <summary>
    /// Creates a circuit breaker policy to prevent cascading failures.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        AiServiceOptions options,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AiServiceClient.CircuitBreaker");

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: options.CircuitBreakerFailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(options.CircuitBreakerDurationSeconds),
                onBreak: (outcome, breakDuration) =>
                {
                    logger.LogError(
                        "AI service circuit breaker OPENED for {BreakDuration}s. " +
                        "StatusCode: {StatusCode}, Exception: {ExceptionMessage}",
                        breakDuration.TotalSeconds,
                        outcome.Result?.StatusCode.ToString() ?? "N/A",
                        outcome.Exception?.Message ?? "N/A");
                },
                onReset: () =>
                {
                    logger.LogInformation("AI service circuit breaker RESET. Service is healthy again.");
                },
                onHalfOpen: () =>
                {
                    logger.LogInformation("AI service circuit breaker HALF-OPEN. Testing service health...");
                });
    }
}

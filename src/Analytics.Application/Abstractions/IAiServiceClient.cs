using Analytics.Application.Models;

namespace Analytics.Application.Abstractions;

/// <summary>
/// Client interface for communicating with the Python AI service.
/// </summary>
public interface IAiServiceClient
{
    /// <summary>
    /// Sends a natural language query to the AI service and receives a generated SQL proposal.
    /// </summary>
    /// <param name="request">The SQL generation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated SQL response or a fallback result on failure.</returns>
    Task<AiServiceResult<GenerateSqlResponse>> GenerateSqlAsync(
        GenerateSqlRequest request,
        CancellationToken cancellationToken = default);
}

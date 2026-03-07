using Analytics.Application.Models;

namespace Analytics.Application.Abstractions;

public interface IFallbackHandler
{
    FallbackInfo HandleAiServiceFailure(
        AiServiceFailureType failureType,
        string failureMessage,
        string originalQuery);

    FallbackInfo HandleValidationFailure(
        SqlValidationResult validationResult,
        string generatedSql,
        string originalQuery);

    FallbackInfo HandleDatabaseFailure(
        Exception exception,
        string executedSql,
        string originalQuery);
}

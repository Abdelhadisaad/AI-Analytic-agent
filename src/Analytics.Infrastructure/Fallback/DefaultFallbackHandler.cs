using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Logging;

namespace Analytics.Infrastructure.Fallback;

public sealed class DefaultFallbackHandler : IFallbackHandler
{
    private readonly ILogger<DefaultFallbackHandler> _logger;

    public DefaultFallbackHandler(ILogger<DefaultFallbackHandler> logger)
    {
        _logger = logger;
    }

    public FallbackInfo HandleAiServiceFailure(
        AiServiceFailureType failureType,
        string failureMessage,
        string originalQuery)
    {
        _logger.LogWarning(
            "AI service failure. Type: {FailureType}, Message: {FailureMessage}, Query: {Query}",
            failureType,
            failureMessage,
            TruncateForLog(originalQuery));

        return failureType switch
        {
            AiServiceFailureType.Timeout => new FallbackInfo
            {
                Reason = FallbackReason.AiServiceTimeout,
                Message = "De AI-service heeft niet op tijd geantwoord. Dit kan komen door een complexe vraag of tijdelijke drukte.",
                SuggestedAction = "Probeer de vraag opnieuw of formuleer deze eenvoudiger.",
                IsRetryable = true
            },

            AiServiceFailureType.Unreachable or AiServiceFailureType.ServiceError => new FallbackInfo
            {
                Reason = FallbackReason.AiServiceUnavailable,
                Message = "De AI-service is tijdelijk niet beschikbaar.",
                SuggestedAction = "Probeer het over enkele minuten opnieuw.",
                IsRetryable = true
            },

            AiServiceFailureType.InvalidResponse => new FallbackInfo
            {
                Reason = FallbackReason.InvalidAiResponse,
                Message = "De AI heeft een ongeldig antwoord gegeven dat niet verwerkt kon worden.",
                SuggestedAction = "Probeer de vraag anders te formuleren.",
                IsRetryable = true
            },

            AiServiceFailureType.RetriesExhausted => new FallbackInfo
            {
                Reason = FallbackReason.AiServiceUnavailable,
                Message = "De AI-service is na meerdere pogingen nog steeds niet bereikbaar.",
                SuggestedAction = "Probeer het later opnieuw of neem contact op met support.",
                IsRetryable = false
            },

            _ => new FallbackInfo
            {
                Reason = FallbackReason.AiServiceUnavailable,
                Message = "Er is een onverwachte fout opgetreden bij de AI-service.",
                SuggestedAction = "Probeer het opnieuw.",
                IsRetryable = true
            }
        };
    }

    public FallbackInfo HandleValidationFailure(
        SqlValidationResult validationResult,
        string generatedSql,
        string originalQuery)
    {
        var hasBlockedKeywords = validationResult.Errors.Any(e =>
            e.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("DELETE", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("DROP", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("ALTER", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase));

        if (hasBlockedKeywords)
        {
            _logger.LogWarning(
                "Unsafe SQL detected. Errors: {Errors}, Query: {Query}",
                string.Join("; ", validationResult.Errors),
                TruncateForLog(originalQuery));

            return new FallbackInfo
            {
                Reason = FallbackReason.UnsafeSqlDetected,
                Message = "De gegenereerde query bevat operaties die niet zijn toegestaan (alleen SELECT-queries zijn toegestaan).",
                SuggestedAction = "Herformuleer uw vraag zodat deze alleen gegevens opvraagt, niet wijzigt.",
                ValidationErrors = validationResult.Errors,
                IsRetryable = true
            };
        }

        if (validationResult.SuspiciousPatterns.Any())
        {
            _logger.LogWarning(
                "Suspicious SQL patterns detected. Patterns: {Patterns}, Query: {Query}",
                string.Join("; ", validationResult.SuspiciousPatterns),
                TruncateForLog(originalQuery));

            return new FallbackInfo
            {
                Reason = FallbackReason.SuspiciousPatternsDetected,
                Message = "De gegenereerde query bevat verdachte patronen die handmatige controle vereisen.",
                SuggestedAction = "Controleer uw vraag op ongebruikelijke syntax of neem contact op met support.",
                SuspiciousPatterns = validationResult.SuspiciousPatterns,
                IsRetryable = false
            };
        }

        _logger.LogWarning(
            "SQL validation failed. Errors: {Errors}, Query: {Query}",
            string.Join("; ", validationResult.Errors),
            TruncateForLog(originalQuery));

        return new FallbackInfo
        {
            Reason = FallbackReason.SqlValidationFailed,
            Message = "De gegenereerde SQL-query kon niet worden gevalideerd.",
            SuggestedAction = "Probeer uw vraag anders te formuleren.",
            ValidationErrors = validationResult.Errors,
            IsRetryable = true
        };
    }

    public FallbackInfo HandleDatabaseFailure(
        Exception exception,
        string executedSql,
        string originalQuery)
    {
        _logger.LogError(
            exception,
            "Database execution failed. SQL: {Sql}, Query: {Query}",
            TruncateForLog(executedSql),
            TruncateForLog(originalQuery));

        var message = exception.Message;

        if (message.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("read-only", StringComparison.OrdinalIgnoreCase))
        {
            return new FallbackInfo
            {
                Reason = FallbackReason.DatabaseExecutionFailed,
                Message = "De query is afgewezen door de database vanwege beveiligingsbeperkingen.",
                SuggestedAction = "Deze operatie is niet toegestaan. Vraag alleen leesbare gegevens op.",
                IsRetryable = false
            };
        }

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("canceling statement", StringComparison.OrdinalIgnoreCase))
        {
            return new FallbackInfo
            {
                Reason = FallbackReason.DatabaseExecutionFailed,
                Message = "De query duurde te lang en is afgebroken.",
                SuggestedAction = "Probeer de vraag specifieker te maken om minder gegevens op te vragen.",
                IsRetryable = true
            };
        }

        if (message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return new FallbackInfo
            {
                Reason = FallbackReason.DatabaseExecutionFailed,
                Message = "De query verwijst naar een tabel of kolom die niet bestaat.",
                SuggestedAction = "Controleer de namen in uw vraag of vraag welke gegevens beschikbaar zijn.",
                IsRetryable = true
            };
        }

        return new FallbackInfo
        {
            Reason = FallbackReason.DatabaseExecutionFailed,
            Message = "Er is een fout opgetreden bij het uitvoeren van de query.",
            SuggestedAction = "Probeer de vraag anders te formuleren.",
            IsRetryable = true
        };
    }

    private static string TruncateForLog(string text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

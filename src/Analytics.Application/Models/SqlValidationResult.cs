namespace Analytics.Application.Models;

public sealed record SqlValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> SuspiciousPatterns)
{
    public static SqlValidationResult Valid(IReadOnlyList<string>? suspiciousPatterns = null)
        => new(true, Array.Empty<string>(), suspiciousPatterns ?? Array.Empty<string>());

    public static SqlValidationResult Invalid(IReadOnlyList<string> errors, IReadOnlyList<string>? suspiciousPatterns = null)
        => new(false, errors, suspiciousPatterns ?? Array.Empty<string>());
}

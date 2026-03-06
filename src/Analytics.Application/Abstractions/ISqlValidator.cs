using Analytics.Application.Models;

namespace Analytics.Application.Abstractions;

public interface ISqlValidator
{
    SqlValidationResult Validate(string sql);
}

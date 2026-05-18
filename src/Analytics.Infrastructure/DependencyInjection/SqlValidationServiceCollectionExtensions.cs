using Analytics.Application.Abstractions;
using Analytics.Infrastructure.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Infrastructure.DependencyInjection;

public static class SqlValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the defense-in-depth SQL validation pipeline:
    /// <list type="number">
    ///   <item><see cref="AstSqlValidator"/> — primary structural validation via AST parsing</item>
    ///   <item><see cref="RegexSqlValidator"/> — secondary heuristic pattern detection</item>
    ///   <item><see cref="CompositeSqlValidator"/> — orchestrates both layers</item>
    /// </list>
    /// The <see cref="ISqlValidator"/> interface resolves to <see cref="CompositeSqlValidator"/>.
    /// </summary>
    public static IServiceCollection AddSqlValidation(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SqlValidationOptions>(configuration.GetSection("SqlValidation"));
        services.AddSingleton<RegexSqlValidator>();
        services.AddSingleton<AstSqlValidator>();
        services.AddSingleton<ISqlValidator, CompositeSqlValidator>();
        return services;
    }
}

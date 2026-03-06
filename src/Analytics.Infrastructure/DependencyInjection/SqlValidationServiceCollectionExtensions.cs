using Analytics.Application.Abstractions;
using Analytics.Infrastructure.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Infrastructure.DependencyInjection;

public static class SqlValidationServiceCollectionExtensions
{
    public static IServiceCollection AddSqlValidation(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SqlValidationOptions>(configuration.GetSection("SqlValidation"));
        services.AddSingleton<ISqlValidator, RegexSqlValidator>();
        return services;
    }
}

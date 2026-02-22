using Analytics.Application.Abstractions;
using Analytics.Infrastructure.Persistence.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Infrastructure.DependencyInjection;

public static class DatabaseProfileServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseProfileResolution(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseProfilesOptions>(configuration.GetSection("DatabaseProfiles"));
        services.AddSingleton<IDatabaseProfileResolver, ConfigDatabaseProfileResolver>();
        return services;
    }
}

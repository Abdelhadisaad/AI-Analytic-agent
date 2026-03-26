using Analytics.Application.Abstractions;
using Analytics.Infrastructure.Persistence.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Infrastructure.DependencyInjection;

public static class SchemaDiscoveryServiceCollectionExtensions
{
    public static IServiceCollection AddSchemaDiscovery(this IServiceCollection services)
    {
        services.AddScoped<ISchemaDiscoveryService, NpgsqlSchemaDiscoveryService>();
        return services;
    }
}

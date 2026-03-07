using Analytics.Application.Abstractions;
using Analytics.Infrastructure.Persistence.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Infrastructure.DependencyInjection;

public static class ReadOnlyExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddReadOnlyQueryExecution(this IServiceCollection services)
    {
        services.AddScoped<IReadOnlyQueryExecutor, NpgsqlReadOnlyQueryExecutor>();
        return services;
    }
}

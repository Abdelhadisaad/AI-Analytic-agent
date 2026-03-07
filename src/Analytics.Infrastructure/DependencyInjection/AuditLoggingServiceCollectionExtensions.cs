using Analytics.Application.Abstractions;
using Analytics.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Infrastructure.DependencyInjection;

public static class AuditLoggingServiceCollectionExtensions
{
    public static IServiceCollection AddAnalyticsAuditLogging(this IServiceCollection services)
    {
        services.AddSingleton<IAnalyticsAuditLogger, StructuredAnalyticsAuditLogger>();
        return services;
    }
}

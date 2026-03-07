using Analytics.Application.Abstractions;
using Analytics.Infrastructure.Fallback;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Infrastructure.DependencyInjection;

public static class FallbackHandlerServiceCollectionExtensions
{
    public static IServiceCollection AddFallbackHandler(this IServiceCollection services)
    {
        services.AddSingleton<IFallbackHandler, DefaultFallbackHandler>();
        return services;
    }
}

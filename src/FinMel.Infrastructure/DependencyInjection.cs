using FinMel.Application.Abstractions.Services;
using FinMel.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FinMel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }
}

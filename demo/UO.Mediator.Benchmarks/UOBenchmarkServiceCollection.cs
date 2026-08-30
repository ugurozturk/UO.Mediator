using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Dispatching;

namespace UO.Mediator.Benchmarks;

internal static class UOBenchmarkServiceCollection
{
    public static ServiceCollection Create(
        bool includeRequestLogging,
        params Assembly[] assemblies)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUOMediator(assemblies);

        if (includeRequestLogging)
        {
            services.AddUOMediatorRequestLogging();
        }

        return services;
    }
}

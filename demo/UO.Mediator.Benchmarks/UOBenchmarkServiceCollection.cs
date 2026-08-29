using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Dispatching;

namespace UO.Mediator.Benchmarks;

internal static class UOBenchmarkServiceCollection
{
    public static ServiceCollection Create(
        bool includeDefaultLogging,
        params Assembly[] assemblies)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUOMediator(assemblies);

        if (!includeDefaultLogging)
        {
            var loggingBehavior = services.Single(descriptor =>
                descriptor.ServiceType == typeof(IRequestBehavior<,>) &&
                descriptor.ImplementationType == typeof(RequestLoggingBehavior<,>));
            services.Remove(loggingBehavior);
        }

        return services;
    }
}

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    // Scans assemblies for concrete types implementing ITransientDependency and registers
    // each implemented interface (except the marker) as transient to the concrete implementation.
    public static IServiceCollection AddAbpStyleDependencies(this IServiceCollection services, params Assembly[]? assemblies)
    {
        var asmList = (assemblies == null || assemblies.Length == 0)
            ? new[] { Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly() }
            : assemblies;

        var marker = typeof(ITransientDependency);

        foreach (var asm in asmList.Where(a => a != null))
        {
            var types = asm!
                .GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && marker.IsAssignableFrom(t));

            foreach (var impl in types)
            {
                var interfaces = impl.GetInterfaces().Where(i => i != marker).ToArray();
                // register concrete type
                services.AddTransient(impl);

                foreach (var @interface in interfaces)
                {
                    services.AddTransient(@interface, impl);
                }
            }
        }

        return services;
    }
}

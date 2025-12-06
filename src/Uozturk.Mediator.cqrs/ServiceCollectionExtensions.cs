using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Uozturk.Mediator.Cqrs
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Scans assemblies for concrete handler and pipeline types and registers
        /// ICommandHandler<,>, IQueryHandler<,> and IPipelineBehavior<,> implementations.
        /// This intentionally does NOT require a marker interface such as ITransientDependency.
        /// </summary>
        public static IServiceCollection AddCustomMediatorDependencies(this IServiceCollection services, params Assembly[]? assemblies)
        {
            var asmList = (assemblies == null || assemblies.Length == 0)
                ? new[] { Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly() }
                : assemblies;

            var commandHandlerOpen = typeof(ICommandHandler<,>);
            var commandHandlerOpen2 = typeof(ICommandHandler<>);
            var queryHandlerOpen = typeof(IQueryHandler<,>);
            var pipelineBehaviorOpen = typeof(IPipelineBehavior<,>);
            var pipelineBehaviorOpen2 = typeof(IPipelineBehavior<>);

            foreach (var asm in asmList.Where(a => a != null))
            {
                var types = asm!
                    .GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
                    .ToArray();

                foreach (var impl in types)
                {
                    var interfaces = impl.GetInterfaces();

                    var implementedHandlerInterfaces = interfaces
                        .Where(i => i.IsGenericType && (
                            i.GetGenericTypeDefinition() == commandHandlerOpen ||
                            i.GetGenericTypeDefinition() == commandHandlerOpen2 ||
                            i.GetGenericTypeDefinition() == queryHandlerOpen ||
                            i.GetGenericTypeDefinition() == pipelineBehaviorOpen ||
                            i.GetGenericTypeDefinition() == pipelineBehaviorOpen2))
                        .ToArray();

                    if (implementedHandlerInterfaces.Length == 0)
                        continue;

                    services.AddTransient(impl);

                    foreach (var @interface in implementedHandlerInterfaces)
                    {
                        services.AddTransient(@interface, impl);
                    }
                }
            }

            return services;
        }

        public static IServiceCollection AddCustomMediatorDependencies(this IServiceCollection services, params Type[] types)
        {
            if (types == null || types.Length == 0) return services.AddCustomMediatorDependencies((Assembly[]?)null);
            var assemblies = types.Select(t => t.Assembly).Distinct().ToArray();
            return services.AddCustomMediatorDependencies(assemblies);
        }
    }
}

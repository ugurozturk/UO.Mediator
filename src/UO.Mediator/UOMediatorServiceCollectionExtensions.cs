using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UO.Mediator.Dispatching;

namespace UO.Mediator;

/// <summary>
/// Registration API for the UO.Mediator dispatcher.
/// </summary>
public static class UOMediatorServiceCollectionExtensions
{
    public static IServiceCollection AddUOMediator(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        return services.AddUOMediator(_ => { }, assemblies);
    }

    public static IServiceCollection AddUOMediator(
        this IServiceCollection services,
        Action<RequestDispatcherOptions> configure,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddTransient<IRequestDispatcher, RequestDispatcher>();
        services.TryAddTransient<RequestGraphValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IRequestBehavior<,>),
            typeof(RequestLoggingBehavior<,>)));

        services.Configure<RequestDispatcherOptions>(options =>
        {
            foreach (var assembly in assemblies.Distinct())
            {
                if (!options.Assemblies.Contains(assembly))
                {
                    options.Assemblies.Add(assembly);
                }
            }

            configure(options);
        });

        foreach (var type in assemblies.Distinct().SelectMany(GetLoadableTypes))
        {
            AddRequestServiceType(services, type);
        }

        return services;
    }

    public static IServiceProvider ValidateUOMediator(this IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        serviceProvider.GetRequiredService<RequestGraphValidator>().Validate();
        return serviceProvider;
    }

    private static void AddRequestServiceType(IServiceCollection services, Type type)
    {
        if (type is { IsAbstract: true } or { IsInterface: true } || type.ContainsGenericParameters)
        {
            return;
        }

        foreach (var serviceType in type.GetInterfaces().Where(IsRequestService))
        {
            services.TryAddEnumerable(ServiceDescriptor.Transient(serviceType, type));
        }
    }

    private static bool IsRequestService(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IRequestHandler<>) ||
               definition == typeof(IRequestHandler<,>) ||
               definition == typeof(IRequestBehavior<,>);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}

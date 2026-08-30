using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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

        AddRuntimeCore(services);

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

    /// <summary>
    /// Registers the mediator runtime without scanning assemblies. This method is intended
    /// for source-generated registration code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddUOMediatorCore(
        this IServiceCollection services,
        Action<RequestDispatcherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<IRequestDispatcher, GeneratedRequestDispatcher>();
        AddCommonCore(services);
        services.Configure(configure ?? (_ => { }));
        return services;
    }

    /// <summary>
    /// Registers the mediator runtime with source-generated strongly typed routes.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddUOMediatorGeneratedRoutes(
        this IServiceCollection services,
        Action<RequestDispatcherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<IRequestDispatcher, GeneratedRoutingRequestDispatcher>();
        AddCommonCore(services, addOpenLoggingBehavior: false);
        services.Configure(configure ?? (_ => { }));
        return services;
    }

    private static void AddRuntimeCore(IServiceCollection services)
    {
        services.TryAddTransient<IRequestDispatcher, RequestDispatcher>();
        AddCommonCore(services);
    }

    private static void AddCommonCore(
        IServiceCollection services,
        bool addOpenLoggingBehavior = true)
    {
        services.TryAddTransient<RequestGraphValidator>();
        services.TryAddSingleton<RequestPipelineCache>();
        services.TryAddSingleton<RequestExecutorRegistry>();
        if (addOpenLoggingBehavior)
        {
            services.TryAddEnumerable(ServiceDescriptor.Transient(
                typeof(IRequestBehavior<,>),
                typeof(RequestLoggingBehavior<,>)));
        }
    }

    /// <summary>
    /// Creates the per-assembly registration index used by generated code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static GeneratedServiceRegistrationState CreateGeneratedRegistrationState(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new GeneratedServiceRegistrationState(services);
    }

    /// <summary>
    /// Registers a source-generated request handler and its strongly typed executor.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddGeneratedRequest<
        TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services,
        GeneratedServiceRegistrationState? registrationState = null)
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        ArgumentNullException.ThrowIfNull(services);

        var state = registrationState ?? new GeneratedServiceRegistrationState(services);
        state.TryAddTransient(
            services,
            typeof(IRequestHandler<TRequest>),
            typeof(THandler));
        state.TryAddTransient(
            services,
            typeof(IRequestHandler<TRequest, Unit>),
            typeof(THandler));
        state.TryAddSingleton(
            services,
            typeof(IRequestExecutor),
            new NoResponseRequestExecutor<TRequest>());
        state.TryAddSingleton(
            services,
            typeof(IRequestExecutor),
            new RequestExecutor<TRequest, Unit>());
        return services;
    }

    /// <summary>
    /// Registers a no-response handler whose partial request owns its generated route.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddGeneratedRoutedRequest<
        TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services,
        GeneratedServiceRegistrationState? registrationState = null)
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        ArgumentNullException.ThrowIfNull(services);

        var state = registrationState ?? new GeneratedServiceRegistrationState(services);
        state.TryAddTransient(
            services,
            typeof(IRequestHandler<TRequest>),
            typeof(THandler));
        state.TryAddTransient(
            services,
            typeof(IRequestHandler<TRequest, Unit>),
            typeof(THandler));
        state.TryAddSingleton(
            services,
            typeof(IGeneratedRequestDescriptor),
            new GeneratedNoResponseRequestDescriptor<TRequest>());
        return services;
    }

    /// <summary>
    /// Registers a response handler whose partial request owns its generated route.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddGeneratedRoutedRequest<
        TRequest,
        TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services,
        GeneratedServiceRegistrationState? registrationState = null)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        var state = registrationState ?? new GeneratedServiceRegistrationState(services);
        state.TryAddTransient(
            services,
            typeof(IRequestHandler<TRequest, TResponse>),
            typeof(THandler));
        state.TryAddSingleton(
            services,
            typeof(IGeneratedRequestDescriptor),
            new GeneratedRequestDescriptor<TRequest, TResponse>());
        return services;
    }

    /// <summary>
    /// Registers a source-generated response handler and its strongly typed executor.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddGeneratedRequest<
        TRequest,
        TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services,
        GeneratedServiceRegistrationState? registrationState = null)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        var state = registrationState ?? new GeneratedServiceRegistrationState(services);
        state.TryAddTransient(
            services,
            typeof(IRequestHandler<TRequest, TResponse>),
            typeof(THandler));
        state.TryAddSingleton(
            services,
            typeof(IRequestExecutor),
            new RequestExecutor<TRequest, TResponse>());
        return services;
    }

    /// <summary>
    /// Registers a source-generated closed request behavior.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddGeneratedBehavior<
        TRequest,
        TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>(
        this IServiceCollection services,
        GeneratedServiceRegistrationState? registrationState = null)
        where TRequest : IRequest<TResponse>
        where TBehavior : class, IRequestBehavior<TRequest, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        var state = registrationState ?? new GeneratedServiceRegistrationState(services);
        state.TryAddTransient(
            services,
            typeof(IRequestBehavior<TRequest, TResponse>),
            typeof(TBehavior));
        return services;
    }

    /// <summary>
    /// Registers the default logging behavior as a NativeAOT-safe closed generic service.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddGeneratedLoggingBehavior<TRequest, TResponse>(
        this IServiceCollection services,
        GeneratedServiceRegistrationState? registrationState = null)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        var state = registrationState ?? new GeneratedServiceRegistrationState(services);
        state.TryAddTransient(
            services,
            typeof(IRequestBehavior<TRequest, TResponse>),
            typeof(RequestLoggingBehavior<TRequest, TResponse>));
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

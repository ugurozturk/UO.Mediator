using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Dispatching;
using UO.Mediator.Generated;
using Xunit;

namespace UO.Mediator.Generators.IntegrationTests;

public class GeneratedDispatchIntegrationTests
{
    [Fact]
    public void Should_Not_Duplicate_Handler_Or_Behavior_Registrations()
    {
        var services = CreateServices();

        services.AddGeneratorsIntegrationTestsUOMediator();
        services.AddGeneratorsIntegrationTestsUOMediator();

        Assert.Single(services, descriptor =>
            descriptor.ServiceType ==
            typeof(IRequestHandler<TransientRouteRequest, Guid>));
        Assert.Equal(2, services.Count(descriptor =>
            descriptor.ServiceType ==
            typeof(IRequestBehavior<TransientRouteRequest, Guid>)));
    }

    [Fact]
    public async Task Should_Resolve_Handlers_And_Behaviors_Using_Configured_Lifetimes()
    {
        var services = CreateServices();
        services.AddTransient<IRequestHandler<TransientRouteRequest, Guid>, TransientRouteHandler>();
        services.AddTransient<IRequestBehavior<TransientRouteRequest, Guid>, TransientRouteBehavior>();
        services.AddScoped<IRequestHandler<ScopedRouteRequest, Guid>, ScopedRouteHandler>();
        services.AddScoped<IRequestBehavior<ScopedRouteRequest, Guid>, ScopedRouteBehavior>();
        services.AddSingleton<IRequestHandler<SingletonRouteRequest, Guid>, SingletonRouteHandler>();
        services.AddSingleton<IRequestBehavior<SingletonRouteRequest, Guid>, SingletonRouteBehavior>();
        services.AddGeneratorsIntegrationTestsUOMediator();
        RemoveDefaultLoggingBehaviors(services);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var firstTransient = await DispatchTwiceAsync(provider, static trace => new TransientRouteRequest(trace));
        var secondTransient = await DispatchOnceAsync(provider, static trace => new TransientRouteRequest(trace));
        var firstScoped = await DispatchTwiceAsync(provider, static trace => new ScopedRouteRequest(trace));
        var secondScoped = await DispatchOnceAsync(provider, static trace => new ScopedRouteRequest(trace));
        var firstSingleton = await DispatchTwiceAsync(provider, static trace => new SingletonRouteRequest(trace));
        var secondSingleton = await DispatchOnceAsync(provider, static trace => new SingletonRouteRequest(trace));

        Assert.NotEqual(firstTransient[0], firstTransient[2]);
        Assert.NotEqual(firstTransient[0], secondTransient[0]);
        Assert.NotEqual(firstTransient[1], firstTransient[3]);
        Assert.NotEqual(firstTransient[1], secondTransient[1]);

        Assert.Equal(firstScoped[0], firstScoped[2]);
        Assert.NotEqual(firstScoped[0], secondScoped[0]);
        Assert.Equal(firstScoped[1], firstScoped[3]);
        Assert.NotEqual(firstScoped[1], secondScoped[1]);

        Assert.Equal(firstSingleton[0], firstSingleton[2]);
        Assert.Equal(firstSingleton[0], secondSingleton[0]);
        Assert.Equal(firstSingleton[1], firstSingleton[3]);
        Assert.Equal(firstSingleton[1], secondSingleton[1]);
    }

    [Fact]
    public async Task Should_Preserve_NoResponse_Fast_Path_And_Unit_Dispatch()
    {
        var services = CreateServices();
        services.AddGeneratorsIntegrationTestsUOMediator();
        RemoveDefaultLoggingBehaviors(services);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var direct = new GeneratedNoResponseCommand();
        var generic = new GeneratedNoResponseCommand();

        await dispatcher.DispatchAsync(direct);
        var unit = await dispatcher.DispatchAsync<Unit>(generic);

        Assert.IsAssignableFrom<IGeneratedRequestRoute>(direct);
        Assert.IsAssignableFrom<IGeneratedRequestRoute<Unit>>(generic);
        Assert.Equal(1, direct.HandleCount);
        Assert.Equal(1, generic.HandleCount);
        Assert.Equal(Unit.Value, unit);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static async Task<Guid[]> DispatchTwiceAsync<TRequest>(
        ServiceProvider provider,
        Func<List<Guid>, TRequest> createRequest)
        where TRequest : IRequest<Guid>
    {
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var trace = new List<Guid>();
        await dispatcher.DispatchAsync(createRequest(trace));
        await dispatcher.DispatchAsync(createRequest(trace));
        return trace.ToArray();
    }

    private static async Task<Guid[]> DispatchOnceAsync<TRequest>(
        ServiceProvider provider,
        Func<List<Guid>, TRequest> createRequest)
        where TRequest : IRequest<Guid>
    {
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var trace = new List<Guid>();
        await dispatcher.DispatchAsync(createRequest(trace));
        return trace.ToArray();
    }

    private static void RemoveDefaultLoggingBehaviors(IServiceCollection services)
    {
        var descriptors = services.Where(descriptor =>
            descriptor.ImplementationType is { IsGenericType: true } implementation &&
            implementation.GetGenericTypeDefinition() == typeof(RequestLoggingBehavior<,>))
            .ToArray();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}

public sealed partial record TransientRouteRequest(List<Guid> Trace) : IRequest<Guid>;

public sealed class TransientRouteHandler : IRequestHandler<TransientRouteRequest, Guid>
{
    private readonly Guid _id = Guid.NewGuid();

    public Task<Guid> HandleAsync(TransientRouteRequest request)
    {
        request.Trace.Add(_id);
        return Task.FromResult(_id);
    }
}

public sealed class TransientRouteBehavior : IRequestBehavior<TransientRouteRequest, Guid>
{
    private readonly Guid _id = Guid.NewGuid();

    public async Task<Guid> HandleAsync(TransientRouteRequest request, RequestHandlerNext<TransientRouteRequest, Guid> next)
    {
        var result = await next.InvokeAsync();
        request.Trace.Add(_id);
        return result;
    }
}

public sealed partial record ScopedRouteRequest(List<Guid> Trace) : IRequest<Guid>;

public sealed class ScopedRouteHandler : IRequestHandler<ScopedRouteRequest, Guid>
{
    private readonly Guid _id = Guid.NewGuid();

    public Task<Guid> HandleAsync(ScopedRouteRequest request)
    {
        request.Trace.Add(_id);
        return Task.FromResult(_id);
    }
}

public sealed class ScopedRouteBehavior : IRequestBehavior<ScopedRouteRequest, Guid>
{
    private readonly Guid _id = Guid.NewGuid();

    public async Task<Guid> HandleAsync(ScopedRouteRequest request, RequestHandlerNext<ScopedRouteRequest, Guid> next)
    {
        var result = await next.InvokeAsync();
        request.Trace.Add(_id);
        return result;
    }
}

public sealed partial record SingletonRouteRequest(List<Guid> Trace) : IRequest<Guid>;

public sealed class SingletonRouteHandler : IRequestHandler<SingletonRouteRequest, Guid>
{
    private readonly Guid _id = Guid.NewGuid();

    public Task<Guid> HandleAsync(SingletonRouteRequest request)
    {
        request.Trace.Add(_id);
        return Task.FromResult(_id);
    }
}

public sealed class SingletonRouteBehavior : IRequestBehavior<SingletonRouteRequest, Guid>
{
    private readonly Guid _id = Guid.NewGuid();

    public async Task<Guid> HandleAsync(SingletonRouteRequest request, RequestHandlerNext<SingletonRouteRequest, Guid> next)
    {
        var result = await next.InvokeAsync();
        request.Trace.Add(_id);
        return result;
    }
}

public sealed partial class GeneratedNoResponseCommand : IRequest
{
    public int HandleCount { get; set; }
}

public sealed class GeneratedNoResponseCommandHandler : IRequestHandler<GeneratedNoResponseCommand>
{
    public Task HandleAsync(GeneratedNoResponseCommand request)
    {
        request.HandleCount++;
        return Task.CompletedTask;
    }
}

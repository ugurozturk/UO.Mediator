using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Dispatching;
using UO.Mediator.Generated;
using Startup10 = UO.Mediator.Startup10;
using Startup100 = UO.Mediator.Startup100;
using Startup1000 = UO.Mediator.Startup1000;

namespace UO.Mediator.Benchmarks;

[MemoryDiagnoser]
public class UOStartupRegistrationBenchmarks
{
    private ServiceCollection _runtimeServices = null!;
    private ServiceCollection _generatedServices = null!;

    [Params(10, 100, 1000)]
    public int HandlerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _runtimeServices = CreateRuntimeServices(HandlerCount);
        _generatedServices = CreateGeneratedServices(HandlerCount);
    }

    [Benchmark(Baseline = true, Description = "Runtime reflection registration")]
    public int RuntimeRegistration() => CreateRuntimeServices(HandlerCount).Count;

    [Benchmark(Description = "Source-generated registration")]
    public int GeneratedRegistration() => CreateGeneratedServices(HandlerCount).Count;

    [Benchmark(Description = "Runtime ServiceProvider build")]
    public ServiceProvider RuntimeProviderBuild() =>
        _runtimeServices.BuildServiceProvider(validateScopes: true);

    [Benchmark(Description = "Generated ServiceProvider build")]
    public ServiceProvider GeneratedProviderBuild() =>
        _generatedServices.BuildServiceProvider(validateScopes: true);

    internal static ServiceCollection CreateRuntimeServices(int handlerCount)
    {
        var services = new ServiceCollection();
        services.AddUOMediator(GetHandlerAssembly(handlerCount));
        RemoveLoggingBehavior(services);
        return services;
    }

    internal static ServiceCollection CreateGeneratedServices(int handlerCount)
    {
        var services = new ServiceCollection();
        switch (handlerCount)
        {
            case 10:
                services.AddStartup10UOMediator();
                break;
            case 100:
                services.AddStartup100UOMediator();
                break;
            case 1000:
                services.AddStartup1000UOMediator();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(handlerCount));
        }

        RemoveLoggingBehavior(services);
        return services;
    }

    internal static IRequest<int> CreateLastRequest(int handlerCount)
    {
        return handlerCount switch
        {
            10 => new Startup10.Request0010(42),
            100 => new Startup100.Request0100(42),
            1000 => new Startup1000.Request1000(42),
            _ => throw new ArgumentOutOfRangeException(nameof(handlerCount))
        };
    }

    private static Assembly GetHandlerAssembly(int handlerCount)
    {
        return handlerCount switch
        {
            10 => typeof(Startup10.Handler0010).Assembly,
            100 => typeof(Startup100.Handler0100).Assembly,
            1000 => typeof(Startup1000.Handler1000).Assembly,
            _ => throw new ArgumentOutOfRangeException(nameof(handlerCount))
        };
    }

    private static void RemoveLoggingBehavior(IServiceCollection services)
    {
        var loggingBehaviors = services.Where(descriptor =>
        {
            var implementationType = descriptor.ImplementationType;
            return implementationType == typeof(RequestLoggingBehavior<,>) ||
                   implementationType is { IsGenericType: true } &&
                   implementationType.GetGenericTypeDefinition() ==
                   typeof(RequestLoggingBehavior<,>);
        }).ToArray();

        foreach (var loggingBehavior in loggingBehaviors)
        {
            services.Remove(loggingBehavior);
        }
    }
}

[MemoryDiagnoser]
public class UOStartupDispatchBenchmarks
{
    private static readonly Action ClearRuntimeExecutorCache =
        CreateRuntimeExecutorCacheReset();
    private ServiceProvider _runtimeFirstProvider = null!;
    private ServiceProvider _generatedFirstProvider = null!;
    private ServiceProvider _runtimeWarmedProvider = null!;
    private ServiceProvider _generatedWarmedProvider = null!;
    private IRequestDispatcher _runtimeFirstDispatcher = null!;
    private IRequestDispatcher _generatedFirstDispatcher = null!;
    private IRequestDispatcher _runtimeWarmedDispatcher = null!;
    private IRequestDispatcher _generatedWarmedDispatcher = null!;
    private IRequest<int> _request = null!;

    [Params(10, 100, 1000)]
    public int HandlerCount { get; set; }

    [GlobalSetup]
    public void SetupWarmedProviders()
    {
        _request = UOStartupRegistrationBenchmarks.CreateLastRequest(HandlerCount);
        _runtimeWarmedProvider = UOStartupRegistrationBenchmarks
            .CreateRuntimeServices(HandlerCount)
            .BuildServiceProvider(validateScopes: true);
        _generatedWarmedProvider = UOStartupRegistrationBenchmarks
            .CreateGeneratedServices(HandlerCount)
            .BuildServiceProvider(validateScopes: true);
        _runtimeWarmedDispatcher =
            _runtimeWarmedProvider.GetRequiredService<IRequestDispatcher>();
        _generatedWarmedDispatcher =
            _generatedWarmedProvider.GetRequiredService<IRequestDispatcher>();
        _runtimeWarmedDispatcher.DispatchAsync(_request).GetAwaiter().GetResult();
        _generatedWarmedDispatcher.DispatchAsync(_request).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void CleanupWarmedProviders()
    {
        _runtimeWarmedProvider.Dispose();
        _generatedWarmedProvider.Dispose();
    }

    [IterationSetup(Target = nameof(RuntimeFirstDispatch))]
    public void SetupRuntimeFirstDispatch()
    {
        ClearRuntimeExecutorCache();
        _runtimeFirstProvider = UOStartupRegistrationBenchmarks
            .CreateRuntimeServices(HandlerCount)
            .BuildServiceProvider(validateScopes: true);
        _runtimeFirstDispatcher =
            _runtimeFirstProvider.GetRequiredService<IRequestDispatcher>();
    }

    [IterationCleanup(Target = nameof(RuntimeFirstDispatch))]
    public void CleanupRuntimeFirstDispatch() => _runtimeFirstProvider.Dispose();

    [IterationSetup(Target = nameof(GeneratedFirstDispatch))]
    public void SetupGeneratedFirstDispatch()
    {
        _generatedFirstProvider = UOStartupRegistrationBenchmarks
            .CreateGeneratedServices(HandlerCount)
            .BuildServiceProvider(validateScopes: true);
        _generatedFirstDispatcher =
            _generatedFirstProvider.GetRequiredService<IRequestDispatcher>();
    }

    [IterationCleanup(Target = nameof(GeneratedFirstDispatch))]
    public void CleanupGeneratedFirstDispatch() => _generatedFirstProvider.Dispose();

    [Benchmark(Baseline = true, Description = "Runtime first dispatch")]
    public Task<int> RuntimeFirstDispatch() =>
        _runtimeFirstDispatcher.DispatchAsync(_request);

    [Benchmark(Description = "Generated first dispatch")]
    public Task<int> GeneratedFirstDispatch() =>
        _generatedFirstDispatcher.DispatchAsync(_request);

    [Benchmark(Description = "Runtime warmed dispatch")]
    public Task<int> RuntimeWarmedDispatch() =>
        _runtimeWarmedDispatcher.DispatchAsync(_request);

    [Benchmark(Description = "Generated warmed dispatch")]
    public Task<int> GeneratedWarmedDispatch() =>
        _generatedWarmedDispatcher.DispatchAsync(_request);

    private static Action CreateRuntimeExecutorCacheReset()
    {
        var cacheField = typeof(RequestDispatcher).GetField(
            "Executors",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                "The runtime executor cache field could not be found.");
        var cache = cacheField.GetValue(null) ??
            throw new InvalidOperationException(
                "The runtime executor cache was not initialized.");
        var clearMethod = cache.GetType().GetMethod("Clear", Type.EmptyTypes) ??
            throw new InvalidOperationException(
                "The runtime executor cache does not expose Clear().");

        return () => clearMethod.Invoke(cache, null);
    }
}

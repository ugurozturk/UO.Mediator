using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Benchmarks.Cases;
using UO.Mediator.Dispatching;

namespace UO.Mediator.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class UOBehaviorLifetimeBenchmarks
{
    private readonly BehaviorPipelineRequest _request = new(42);
    private readonly BehaviorPipelineRequestHandler _handler = new();

    private ServiceProvider _zeroBehaviorProvider = null!;
    private ServiceProvider _oneTransientProvider = null!;
    private ServiceProvider _threeTransientProvider = null!;
    private ServiceProvider _fiveTransientProvider = null!;
    private ServiceProvider _oneSingletonProvider = null!;
    private ServiceProvider _threeSingletonProvider = null!;
    private ServiceProvider _fiveSingletonProvider = null!;
    private IRequestDispatcher _zeroBehaviors = null!;
    private IRequestDispatcher _oneTransientBehavior = null!;
    private IRequestDispatcher _threeTransientBehaviors = null!;
    private IRequestDispatcher _fiveTransientBehaviors = null!;
    private IRequestDispatcher _oneSingletonBehavior = null!;
    private IRequestDispatcher _threeSingletonBehaviors = null!;
    private IRequestDispatcher _fiveSingletonBehaviors = null!;

    [GlobalSetup]
    public void Setup()
    {
        (_zeroBehaviorProvider, _zeroBehaviors) = CreateCase(0, ServiceLifetime.Transient);
        (_oneTransientProvider, _oneTransientBehavior) = CreateCase(1, ServiceLifetime.Transient);
        (_threeTransientProvider, _threeTransientBehaviors) = CreateCase(3, ServiceLifetime.Transient);
        (_fiveTransientProvider, _fiveTransientBehaviors) = CreateCase(5, ServiceLifetime.Transient);
        (_oneSingletonProvider, _oneSingletonBehavior) = CreateCase(1, ServiceLifetime.Singleton);
        (_threeSingletonProvider, _threeSingletonBehaviors) = CreateCase(3, ServiceLifetime.Singleton);
        (_fiveSingletonProvider, _fiveSingletonBehaviors) = CreateCase(5, ServiceLifetime.Singleton);

        ZeroBehaviors().GetAwaiter().GetResult();
        OneTransientBehavior().GetAwaiter().GetResult();
        ThreeTransientBehaviors().GetAwaiter().GetResult();
        FiveTransientBehaviors().GetAwaiter().GetResult();
        OneSingletonBehavior().GetAwaiter().GetResult();
        ThreeSingletonBehaviors().GetAwaiter().GetResult();
        FiveSingletonBehaviors().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _zeroBehaviorProvider.Dispose();
        _oneTransientProvider.Dispose();
        _threeTransientProvider.Dispose();
        _fiveTransientProvider.Dispose();
        _oneSingletonProvider.Dispose();
        _threeSingletonProvider.Dispose();
        _fiveSingletonProvider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "0 behaviors")]
    public Task<int> ZeroBehaviors()
    {
        return _zeroBehaviors.DispatchAsync(_request);
    }

    [Benchmark(Description = "1 transient behavior")]
    public Task<int> OneTransientBehavior()
    {
        return _oneTransientBehavior.DispatchAsync(_request);
    }

    [Benchmark(Description = "3 transient behaviors")]
    public Task<int> ThreeTransientBehaviors()
    {
        return _threeTransientBehaviors.DispatchAsync(_request);
    }

    [Benchmark(Description = "5 transient behaviors")]
    public Task<int> FiveTransientBehaviors()
    {
        return _fiveTransientBehaviors.DispatchAsync(_request);
    }

    [Benchmark(Description = "1 singleton behavior")]
    public Task<int> OneSingletonBehavior()
    {
        return _oneSingletonBehavior.DispatchAsync(_request);
    }

    [Benchmark(Description = "3 singleton behaviors")]
    public Task<int> ThreeSingletonBehaviors()
    {
        return _threeSingletonBehaviors.DispatchAsync(_request);
    }

    [Benchmark(Description = "5 singleton behaviors")]
    public Task<int> FiveSingletonBehaviors()
    {
        return _fiveSingletonBehaviors.DispatchAsync(_request);
    }

    private (ServiceProvider Provider, IRequestDispatcher Dispatcher) CreateCase(
        int behaviorCount,
        ServiceLifetime behaviorLifetime)
    {
        var services = UOBenchmarkServiceCollection.Create(includeRequestLogging: false);
        services.AddSingleton<IRequestHandler<BehaviorPipelineRequest, int>>(_handler);

        for (var index = 0; index < behaviorCount; index++)
        {
            if (behaviorLifetime == ServiceLifetime.Transient)
            {
                services.AddTransient<
                    IRequestBehavior<BehaviorPipelineRequest, int>,
                    EmptyBehavior<BehaviorPipelineRequest, int>>();
            }
            else
            {
                services.AddSingleton<
                    IRequestBehavior<BehaviorPipelineRequest, int>,
                    EmptyBehavior<BehaviorPipelineRequest, int>>();
            }
        }

        var provider = services.BuildServiceProvider(validateScopes: true);
        return (provider, provider.GetRequiredService<IRequestDispatcher>());
    }
}

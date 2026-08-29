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
public class UOBehaviorPipelineBenchmarks
{
    private readonly BehaviorPipelineRequest _request = new(42);

    private ServiceProvider _zeroBehaviorProvider = null!;
    private ServiceProvider _oneBehaviorProvider = null!;
    private ServiceProvider _threeBehaviorProvider = null!;
    private ServiceProvider _fiveBehaviorProvider = null!;
    private IRequestDispatcher _zeroBehaviors = null!;
    private IRequestDispatcher _oneBehavior = null!;
    private IRequestDispatcher _threeBehaviors = null!;
    private IRequestDispatcher _fiveBehaviors = null!;

    [GlobalSetup]
    public void Setup()
    {
        (_zeroBehaviorProvider, _zeroBehaviors) = CreateCase(0);
        (_oneBehaviorProvider, _oneBehavior) = CreateCase(1);
        (_threeBehaviorProvider, _threeBehaviors) = CreateCase(3);
        (_fiveBehaviorProvider, _fiveBehaviors) = CreateCase(5);

        ZeroBehaviors().GetAwaiter().GetResult();
        OneEmptyBehavior().GetAwaiter().GetResult();
        ThreeEmptyBehaviors().GetAwaiter().GetResult();
        FiveEmptyBehaviors().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _zeroBehaviorProvider.Dispose();
        _oneBehaviorProvider.Dispose();
        _threeBehaviorProvider.Dispose();
        _fiveBehaviorProvider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "0 behaviors")]
    public Task<int> ZeroBehaviors()
    {
        return _zeroBehaviors.DispatchAsync(_request);
    }

    [Benchmark(Description = "1 empty behavior")]
    public Task<int> OneEmptyBehavior()
    {
        return _oneBehavior.DispatchAsync(_request);
    }

    [Benchmark(Description = "3 empty behaviors")]
    public Task<int> ThreeEmptyBehaviors()
    {
        return _threeBehaviors.DispatchAsync(_request);
    }

    [Benchmark(Description = "5 empty behaviors")]
    public Task<int> FiveEmptyBehaviors()
    {
        return _fiveBehaviors.DispatchAsync(_request);
    }

    private static (ServiceProvider Provider, IRequestDispatcher Dispatcher) CreateCase(int behaviorCount)
    {
        var services = UOBenchmarkServiceCollection.Create(includeDefaultLogging: false);
        services.AddTransient<IRequestHandler<BehaviorPipelineRequest, int>, BehaviorPipelineRequestHandler>();

        for (var index = 0; index < behaviorCount; index++)
        {
            services.AddTransient<
                IRequestBehavior<BehaviorPipelineRequest, int>,
                EmptyBehavior<BehaviorPipelineRequest, int>>();
        }

        var provider = services.BuildServiceProvider(validateScopes: true);
        return (provider, provider.GetRequiredService<IRequestDispatcher>());
    }
}

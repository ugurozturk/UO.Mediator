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
public class UODirectHandlerBaselineBenchmarks
{
    private readonly BehaviorPipelineRequest _request = new(42);
    private readonly BehaviorPipelineRequestHandler _handler = new();

    private ServiceProvider _provider = null!;
    private IRequestDispatcher _dispatcher = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = UOBenchmarkServiceCollection.Create(includeDefaultLogging: false);
        services.AddSingleton<IRequestHandler<BehaviorPipelineRequest, int>>(_handler);

        _provider = services.BuildServiceProvider(validateScopes: true);
        _dispatcher = _provider.GetRequiredService<IRequestDispatcher>();

        DirectHandlerCall().GetAwaiter().GetResult();
        DispatchWithZeroBehaviors().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Direct handler call")]
    public Task<int> DirectHandlerCall()
    {
        return _handler.HandleAsync(_request);
    }

    [Benchmark(Description = "UO.Mediator dispatch with 0 behaviors")]
    public Task<int> DispatchWithZeroBehaviors()
    {
        return _dispatcher.DispatchAsync(_request);
    }
}

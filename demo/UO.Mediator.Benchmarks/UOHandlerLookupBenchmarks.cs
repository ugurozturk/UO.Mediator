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
public class UOHandlerLookupBenchmarks
{
    private ServiceProvider _provider = null!;
    private IRequestDispatcher _dispatcher = null!;
    private IRequest<int> _request = null!;

    [GlobalSetup(Target = nameof(Handlers10))]
    public void Setup10()
    {
        Setup(10);
    }

    [GlobalSetup(Target = nameof(Handlers100))]
    public void Setup100()
    {
        Setup(100);
    }

    [GlobalSetup(Target = nameof(Handlers1000))]
    public void Setup1000()
    {
        Setup(1000);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "10 handlers")]
    public Task<int> Handlers10()
    {
        return _dispatcher.DispatchAsync(_request);
    }

    [Benchmark(Description = "100 handlers")]
    public Task<int> Handlers100()
    {
        return _dispatcher.DispatchAsync(_request);
    }

    [Benchmark(Description = "1000 handlers")]
    public Task<int> Handlers1000()
    {
        return _dispatcher.DispatchAsync(_request);
    }

    private void Setup(int handlerCount)
    {
        var services = UOBenchmarkServiceCollection.Create(includeRequestLogging: false);
        services.AddTransient(typeof(IRequestHandler<,>), typeof(LookupRequestHandler<,>));

        _provider = services.BuildServiceProvider(validateScopes: true);
        _dispatcher = _provider.GetRequiredService<IRequestDispatcher>();

        var requests = LookupRequestFactory.Create(handlerCount);
        foreach (var request in requests)
        {
            _dispatcher.DispatchAsync(request).GetAwaiter().GetResult();
        }

        _request = requests[^1];
    }
}

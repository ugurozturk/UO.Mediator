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
public class UODispatchShapeBenchmarks
{
    private readonly SyncResponseRequest _syncResponseRequest = new(42);
    private readonly SyncCommand _syncCommand = new();
    private readonly YieldResponseRequest _yieldResponseRequest = new(42);
    private readonly YieldCommand _yieldCommand = new();

    private ServiceProvider _provider = null!;
    private IRequestDispatcher _dispatcher = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = UOBenchmarkServiceCollection.Create(includeDefaultLogging: false);
        services.AddTransient<IRequestHandler<SyncResponseRequest, int>, SyncResponseRequestHandler>();
        services.AddTransient<IRequestHandler<SyncCommand>, SyncCommandHandler>();
        services.AddTransient<IRequestHandler<YieldResponseRequest, int>, YieldResponseRequestHandler>();
        services.AddTransient<IRequestHandler<YieldCommand>, YieldCommandHandler>();

        _provider = services.BuildServiceProvider(validateScopes: true);
        _dispatcher = _provider.GetRequiredService<IRequestDispatcher>();

        ResponseSyncCompleted().GetAwaiter().GetResult();
        NoResponseSyncCompleted().GetAwaiter().GetResult();
        ResponseTaskYield().GetAwaiter().GetResult();
        NoResponseTaskYield().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Response / sync-completed Task")]
    public Task<int> ResponseSyncCompleted()
    {
        return _dispatcher.DispatchAsync(_syncResponseRequest);
    }

    [Benchmark(Description = "No response / sync-completed Task")]
    public Task NoResponseSyncCompleted()
    {
        return _dispatcher.DispatchAsync(_syncCommand);
    }

    [Benchmark(Description = "Response / Task.Yield()")]
    public Task<int> ResponseTaskYield()
    {
        return _dispatcher.DispatchAsync(_yieldResponseRequest);
    }

    [Benchmark(Description = "No response / Task.Yield()")]
    public Task NoResponseTaskYield()
    {
        return _dispatcher.DispatchAsync(_yieldCommand);
    }
}

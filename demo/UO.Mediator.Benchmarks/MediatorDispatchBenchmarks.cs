using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;
using MediatR;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Benchmarks.Cases;
using UO.Mediator.Dispatching;
using MediatRSenderContract = MediatR.ISender;
using MartinMediatorContract = Mediator.IMediator;

namespace UO.Mediator.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MediatorDispatchBenchmarks
{
    private readonly UOPingRequest _uoRequest = new(42);
    private readonly MediatRPingRequest _mediatRRequest = new(42);
    private readonly MartinPingRequest _martinRequest = new(42);

    private ServiceProvider _uoDefaultProvider = null!;
    private ServiceProvider _uoCoreProvider = null!;
    private ServiceProvider _mediatRProvider = null!;
    private ServiceProvider _martinProvider = null!;

    private IRequestDispatcher _uoDefault = null!;
    private IRequestDispatcher _uoCore = null!;
    private MediatRSenderContract _mediatR = null!;
    private MartinMediatorContract _martin = null!;

    [GlobalSetup]
    public void Setup()
    {
        _uoDefaultProvider = CreateUOProvider(includeDefaultLogging: true);
        _uoCoreProvider = CreateUOProvider(includeDefaultLogging: false);
        _mediatRProvider = CreateMediatRProvider();
        _martinProvider = CreateMartinProvider();

        _uoDefault = _uoDefaultProvider.GetRequiredService<IRequestDispatcher>();
        _uoCore = _uoCoreProvider.GetRequiredService<IRequestDispatcher>();
        _mediatR = _mediatRProvider.GetRequiredService<MediatRSenderContract>();
        _martin = _martinProvider.GetRequiredService<MartinMediatorContract>();

        _uoDefault.DispatchAsync(_uoRequest).GetAwaiter().GetResult();
        _uoCore.DispatchAsync(_uoRequest).GetAwaiter().GetResult();
        _mediatR.Send(_mediatRRequest).GetAwaiter().GetResult();
        _martin.Send(_martinRequest).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _uoDefaultProvider.Dispose();
        _uoCoreProvider.Dispose();
        _mediatRProvider.Dispose();
        _martinProvider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "UO.Mediator (default logging)")]
    public Task<int> UOMediatorDefault()
    {
        return _uoDefault.DispatchAsync(_uoRequest);
    }

    [Benchmark(Description = "UO.Mediator (core dispatch)")]
    public Task<int> UOMediatorCore()
    {
        return _uoCore.DispatchAsync(_uoRequest);
    }

    [Benchmark(Description = "MediatR")]
    public Task<int> MediatR()
    {
        return _mediatR.Send(_mediatRRequest);
    }

    [Benchmark(Description = "martinothamar/Mediator")]
    public ValueTask<int> MartinMediator()
    {
        return _martin.Send(_martinRequest);
    }

    private static ServiceProvider CreateUOProvider(bool includeDefaultLogging)
    {
        var services = UOBenchmarkServiceCollection.Create(
            includeDefaultLogging,
            typeof(UOPingRequest).Assembly);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider CreateMediatRProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssemblyContaining<MediatRPingRequest>());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider CreateMartinProvider()
    {
        var services = new ServiceCollection();
        services.AddMediator(options =>
        {
            options.Assemblies = [typeof(MartinPingRequest)];
            options.ServiceLifetime = ServiceLifetime.Transient;
        });
        return services.BuildServiceProvider(validateScopes: true);
    }
}

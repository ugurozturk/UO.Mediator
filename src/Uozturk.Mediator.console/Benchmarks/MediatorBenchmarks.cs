using System;
using System.Threading.Tasks;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using Uozturk.Mediator.Cqrs;

namespace MyCompanyName.MyProjectName.Benchmarks;

[MemoryDiagnoser]
public class MediatorBenchmarks
{
    private IMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Register handlers from this assembly
        services.AddCustomMediatorDependencies(typeof(Program).Assembly);

        // Register mediator implementation
        services.AddSingleton<IMediator, Mediator>();

        // Build provider
        var provider = services.BuildServiceProvider();

        _mediator = provider.GetRequiredService<IMediator>();
    }

    [Params(1000, 10000)]
    public int MessageId;

    [Benchmark]
    public Task NonGenericCommand() => _mediator.SendCommandAsync(new PingCommandNonMessage { Message = "ping" + MessageId.ToString() });

    [Benchmark]
    public Task GenericCommand() => _mediator.SendCommandAsync<PingCommand, string>(new PingCommand { Message = "ping" + MessageId.ToString() });

    [Benchmark]
    public Task Query() => _mediator.SendQueryAsync<GetServerTimeQuery, DateTime>(new GetServerTimeQuery());
}

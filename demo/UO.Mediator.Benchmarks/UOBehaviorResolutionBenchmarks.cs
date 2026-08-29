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
public class UOBehaviorResolutionBenchmarks
{
    private ServiceProvider _provider = null!;

    [Params(
        BehaviorResolutionCase.ZeroTransient,
        BehaviorResolutionCase.OneTransient,
        BehaviorResolutionCase.ThreeTransient,
        BehaviorResolutionCase.FiveTransient,
        BehaviorResolutionCase.OneSingleton,
        BehaviorResolutionCase.ThreeSingleton,
        BehaviorResolutionCase.FiveSingleton)]
    public BehaviorResolutionCase Case { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var (behaviorCount, behaviorLifetime) = GetConfiguration(Case);
        var services = new ServiceCollection();

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

        _provider = services.BuildServiceProvider(validateScopes: true);

        _ = GetServicesOnly();
        _ = GetServicesWithCurrentResolveBehaviorsShape();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "GetServices only")]
    public object GetServicesOnly()
    {
        // Microsoft DI eagerly materializes and resolves this collection. Returning it
        // as object lets BenchmarkDotNet consume the result without adding enumeration.
        return _provider.GetServices<IRequestBehavior<BehaviorPipelineRequest, int>>();
    }

    [Benchmark(Description = "GetServices + current ResolveBehaviors conversion")]
    public IReadOnlyList<IRequestBehavior<BehaviorPipelineRequest, int>>
        GetServicesWithCurrentResolveBehaviorsShape()
    {
        var behaviors =
            _provider.GetServices<IRequestBehavior<BehaviorPipelineRequest, int>>();
        return behaviors as IReadOnlyList<IRequestBehavior<BehaviorPipelineRequest, int>>
            ?? behaviors.ToArray();
    }

    private static (int BehaviorCount, ServiceLifetime BehaviorLifetime) GetConfiguration(
        BehaviorResolutionCase benchmarkCase)
    {
        return benchmarkCase switch
        {
            BehaviorResolutionCase.ZeroTransient => (0, ServiceLifetime.Transient),
            BehaviorResolutionCase.OneTransient => (1, ServiceLifetime.Transient),
            BehaviorResolutionCase.ThreeTransient => (3, ServiceLifetime.Transient),
            BehaviorResolutionCase.FiveTransient => (5, ServiceLifetime.Transient),
            BehaviorResolutionCase.OneSingleton => (1, ServiceLifetime.Singleton),
            BehaviorResolutionCase.ThreeSingleton => (3, ServiceLifetime.Singleton),
            BehaviorResolutionCase.FiveSingleton => (5, ServiceLifetime.Singleton),
            _ => throw new ArgumentOutOfRangeException(nameof(benchmarkCase), benchmarkCase, null)
        };
    }
}

public enum BehaviorResolutionCase
{
    ZeroTransient,
    OneTransient,
    ThreeTransient,
    FiveTransient,
    OneSingleton,
    ThreeSingleton,
    FiveSingleton
}

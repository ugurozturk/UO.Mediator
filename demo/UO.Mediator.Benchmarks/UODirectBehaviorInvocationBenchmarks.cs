using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;
using UO.Mediator.Benchmarks.Cases;
using UO.Mediator.Dispatching;

namespace UO.Mediator.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class UODirectBehaviorInvocationBenchmarks
{
    private readonly BehaviorPipelineRequest _request = new(42);
    private readonly BehaviorPipelineRequestHandler _handler = new();
    private readonly EmptyBehavior<BehaviorPipelineRequest, int> _firstBehavior = new();
    private readonly EmptyBehavior<BehaviorPipelineRequest, int> _secondBehavior = new();
    private readonly EmptyBehavior<BehaviorPipelineRequest, int> _thirdBehavior = new();
    private readonly EmptyBehavior<BehaviorPipelineRequest, int> _fourthBehavior = new();
    private readonly EmptyBehavior<BehaviorPipelineRequest, int> _fifthBehavior = new();

    private RequestHandlerDelegate<int> _handlerContinuation = null!;
    private RequestHandlerDelegate<int> _secondOfThreeContinuation = null!;
    private RequestHandlerDelegate<int> _thirdOfThreeContinuation = null!;
    private RequestHandlerDelegate<int> _secondOfFiveContinuation = null!;
    private RequestHandlerDelegate<int> _thirdOfFiveContinuation = null!;
    private RequestHandlerDelegate<int> _fourthOfFiveContinuation = null!;
    private RequestHandlerDelegate<int> _fifthOfFiveContinuation = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Build fixed chains once so this group measures delegate invocation and nested
        // behavior calls, independently of dispatcher, DI, and per-call chain creation.
        _handlerContinuation = () => _handler.HandleAsync(_request);

        _thirdOfThreeContinuation =
            () => _thirdBehavior.HandleAsync(_request, _handlerContinuation);
        _secondOfThreeContinuation =
            () => _secondBehavior.HandleAsync(_request, _thirdOfThreeContinuation);

        _fifthOfFiveContinuation =
            () => _fifthBehavior.HandleAsync(_request, _handlerContinuation);
        _fourthOfFiveContinuation =
            () => _fourthBehavior.HandleAsync(_request, _fifthOfFiveContinuation);
        _thirdOfFiveContinuation =
            () => _thirdBehavior.HandleAsync(_request, _fourthOfFiveContinuation);
        _secondOfFiveContinuation =
            () => _secondBehavior.HandleAsync(_request, _thirdOfFiveContinuation);

        DirectHandler().GetAwaiter().GetResult();
        DirectOneBehavior().GetAwaiter().GetResult();
        DirectThreeBehaviors().GetAwaiter().GetResult();
        DirectFiveBehaviors().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true, Description = "Direct handler")]
    public Task<int> DirectHandler()
    {
        return _handler.HandleAsync(_request);
    }

    [Benchmark(Description = "Direct 1 behavior")]
    public Task<int> DirectOneBehavior()
    {
        return _firstBehavior.HandleAsync(_request, _handlerContinuation);
    }

    [Benchmark(Description = "Direct 3 behaviors")]
    public Task<int> DirectThreeBehaviors()
    {
        return _firstBehavior.HandleAsync(_request, _secondOfThreeContinuation);
    }

    [Benchmark(Description = "Direct 5 behaviors")]
    public Task<int> DirectFiveBehaviors()
    {
        return _firstBehavior.HandleAsync(_request, _secondOfFiveContinuation);
    }
}

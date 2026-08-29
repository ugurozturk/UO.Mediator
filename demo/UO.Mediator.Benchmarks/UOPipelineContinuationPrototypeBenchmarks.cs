using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;
using UO.Mediator.Dispatching;

namespace UO.Mediator.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class UOPipelineContinuationPrototypeBenchmarks
{
    private const int ExpectedResponse = 42;
    private const int ConcurrentDispatchCount = 32;

    private readonly PrototypeRequest _request = new(ExpectedResponse);
    private readonly IPrototypeHandler _handler = new PrototypeHandler();

    private IPrototypeBehavior[] _behaviors = null!;
    private PrototypeNext<PrototypeRequest, int> _cachedRequestPassingPipeline = null!;

    [Params(1, 3, 5)]
    public int BehaviorCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _behaviors = new IPrototypeBehavior[BehaviorCount];

        for (var position = 0; position < _behaviors.Length; position++)
        {
            _behaviors[position] = new PrototypeBehavior();
        }

        // Every architecture shares these benchmark-local singleton instances. The
        // cached architecture changes only the continuation representation/lifetime.
        _cachedRequestPassingPipeline = BuildCachedRequestPassingPipeline(_handler, _behaviors);

        ValidateImplementation(
            "Current captured closure",
            request => ExecuteCapturedClosureAsync(0, request, _handler, _behaviors));
        ValidateImplementation(
            "Readonly struct continuation",
            request => new PrototypeStructContinuation(request, _handler, _behaviors, 0).InvokeAsync());
        ValidateImplementation(
            "Cached request-passing delegate",
            request => _cachedRequestPassingPipeline(request));
    }

    [Benchmark(Baseline = true, Description = "Current captured closure")]
    public Task<int> CurrentCapturedClosure()
    {
        return ExecuteCapturedClosureAsync(0, _request, _handler, _behaviors);
    }

    [Benchmark(Description = "Readonly struct continuation")]
    public Task<int> ReadonlyStructContinuation()
    {
        return new PrototypeStructContinuation(_request, _handler, _behaviors, 0).InvokeAsync();
    }

    [Benchmark(Description = "Cached request-passing delegate")]
    public Task<int> CachedRequestPassingDelegate()
    {
        return _cachedRequestPassingPipeline(_request);
    }

    private static Task<int> ExecuteCapturedClosureAsync(
        int position,
        PrototypeRequest request,
        IPrototypeHandler handler,
        IReadOnlyList<IPrototypeBehavior> behaviors)
    {
        if (position == behaviors.Count)
        {
            return handler.HandleAsync(request);
        }

        request.Probe?.RecordBehavior(position);
        var behavior = behaviors[position];

        return behavior.HandleAsync(
            request,
            () => ExecuteCapturedClosureAsync(position + 1, request, handler, behaviors));
    }

    private static PrototypeNext<PrototypeRequest, int> BuildCachedRequestPassingPipeline(
        IPrototypeHandler handler,
        IReadOnlyList<IPrototypeBehavior> behaviors)
    {
        PrototypeNext<PrototypeRequest, int> next = handler.HandleAsync;

        for (var position = behaviors.Count - 1; position >= 0; position--)
        {
            var step = new CachedRequestPassingStep(position, behaviors[position], next);
            next = step.InvokeAsync;
        }

        return next;
    }

    private void ValidateImplementation(
        string architecture,
        Func<PrototypeRequest, Task<int>> dispatchAsync)
    {
        var probes = new ValidationProbe[ConcurrentDispatchCount];

        Parallel.For(0, ConcurrentDispatchCount, dispatchIndex =>
        {
            var probe = new ValidationProbe(BehaviorCount);
            var request = new PrototypeRequest(ExpectedResponse + dispatchIndex, probe);
            var response = dispatchAsync(request).GetAwaiter().GetResult();

            if (response != request.Value)
            {
                throw new InvalidOperationException(
                    $"{architecture} returned {response} for request {request.Value}.");
            }

            probe.AssertComplete(architecture, dispatchIndex);
            probes[dispatchIndex] = probe;
        });

        if (probes.Distinct().Count() != ConcurrentDispatchCount)
        {
            throw new InvalidOperationException(
                $"{architecture} shared validation state between concurrent dispatches.");
        }

        var benchmarkResponse = dispatchAsync(_request).GetAwaiter().GetResult();

        if (benchmarkResponse != ExpectedResponse)
        {
            throw new InvalidOperationException(
                $"{architecture} returned {benchmarkResponse}; expected {ExpectedResponse}.");
        }
    }

    private sealed record PrototypeRequest(int Value, ValidationProbe? Probe = null);

    private interface IPrototypeHandler
    {
        Task<int> HandleAsync(PrototypeRequest request);
    }

    private sealed class PrototypeHandler : IPrototypeHandler
    {
        public Task<int> HandleAsync(PrototypeRequest request)
        {
            request.Probe?.RecordHandler();
            return Task.FromResult(request.Value);
        }
    }

    private interface IPrototypeBehavior
    {
        Task<int> HandleAsync(
            PrototypeRequest request,
            RequestHandlerDelegate<int> next);

        Task<int> HandleAsync(
            PrototypeRequest request,
            PrototypeStructContinuation next);

        Task<int> HandleAsync(
            PrototypeRequest request,
            PrototypeNext<PrototypeRequest, int> next);
    }

    private sealed class PrototypeBehavior : IPrototypeBehavior
    {
        public Task<int> HandleAsync(
            PrototypeRequest request,
            RequestHandlerDelegate<int> next)
        {
            return next();
        }

        public Task<int> HandleAsync(
            PrototypeRequest request,
            PrototypeStructContinuation next)
        {
            return next.InvokeAsync();
        }

        public Task<int> HandleAsync(
            PrototypeRequest request,
            PrototypeNext<PrototypeRequest, int> next)
        {
            return next(request);
        }
    }

    private readonly struct PrototypeStructContinuation
    {
        private readonly PrototypeRequest _request;
        private readonly IPrototypeHandler _handler;
        private readonly IReadOnlyList<IPrototypeBehavior> _behaviors;
        private readonly int _position;

        public PrototypeStructContinuation(
            PrototypeRequest request,
            IPrototypeHandler handler,
            IReadOnlyList<IPrototypeBehavior> behaviors,
            int position)
        {
            _request = request;
            _handler = handler;
            _behaviors = behaviors;
            _position = position;
        }

        public Task<int> InvokeAsync()
        {
            if (_position == _behaviors.Count)
            {
                return _handler.HandleAsync(_request);
            }

            _request.Probe?.RecordBehavior(_position);
            var behavior = _behaviors[_position];
            var next = new PrototypeStructContinuation(
                _request,
                _handler,
                _behaviors,
                _position + 1);

            return behavior.HandleAsync(_request, next);
        }
    }

    private delegate Task<TResponse> PrototypeNext<in TRequest, TResponse>(TRequest request);

    private sealed class CachedRequestPassingStep
    {
        private readonly int _position;
        private readonly IPrototypeBehavior _behavior;
        private readonly PrototypeNext<PrototypeRequest, int> _next;

        public CachedRequestPassingStep(
            int position,
            IPrototypeBehavior behavior,
            PrototypeNext<PrototypeRequest, int> next)
        {
            _position = position;
            _behavior = behavior;
            _next = next;
        }

        public Task<int> InvokeAsync(PrototypeRequest request)
        {
            request.Probe?.RecordBehavior(_position);
            return _behavior.HandleAsync(request, _next);
        }
    }

    private sealed class ValidationProbe
    {
        private readonly int[] _executionOrder;
        private int _eventCount;
        private int _handlerCount;

        public ValidationProbe(int behaviorCount)
        {
            _executionOrder = new int[behaviorCount];
        }

        public void RecordBehavior(int position)
        {
            if (_eventCount >= _executionOrder.Length)
            {
                throw new InvalidOperationException("A behavior executed more than once.");
            }

            _executionOrder[_eventCount] = position;
            _eventCount++;
        }

        public void RecordHandler()
        {
            _handlerCount++;
        }

        public void AssertComplete(string architecture, int dispatchIndex)
        {
            if (_handlerCount != 1)
            {
                throw new InvalidOperationException(
                    $"{architecture} executed the handler {_handlerCount} times in dispatch {dispatchIndex}.");
            }

            if (_eventCount != _executionOrder.Length)
            {
                throw new InvalidOperationException(
                    $"{architecture} executed {_eventCount} of {_executionOrder.Length} behaviors " +
                    $"in dispatch {dispatchIndex}.");
            }

            for (var position = 0; position < _executionOrder.Length; position++)
            {
                if (_executionOrder[position] != position)
                {
                    throw new InvalidOperationException(
                        $"{architecture} executed behavior {_executionOrder[position]} at position {position} " +
                        $"in dispatch {dispatchIndex}.");
                }
            }
        }
    }
}

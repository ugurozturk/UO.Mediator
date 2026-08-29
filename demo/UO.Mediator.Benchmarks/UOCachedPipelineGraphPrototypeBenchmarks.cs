using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Dispatching;

namespace UO.Mediator.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class UOCachedPipelineGraphPrototypeBenchmarks
{
    private const int ExpectedResponse = 42;

    private readonly CachedGraphPrototypeRequest _request = new(ExpectedResponse);
    private readonly ICachedGraphPrototypeHandler _handler = new CachedGraphPrototypeHandler();

    private ICachedGraphPrototypeBehavior[] _behaviors = null!;
    private CachedGraphPrototypePipeline _cachedGraph = null!;

    [Params(1, 3, 5)]
    public int BehaviorCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _behaviors = Enumerable.Range(0, BehaviorCount)
            .Select(_ => (ICachedGraphPrototypeBehavior)new CachedGraphPrototypeBehavior())
            .ToArray();
        _cachedGraph = CachedGraphPrototypePipeline.Create(BehaviorCount);

        CachedGraphPrototypeValidation.ValidateArchitectures(
            BehaviorCount,
            _handler,
            _behaviors,
            _cachedGraph);
        CachedGraphPrototypeValidation.ValidateConcurrentDispatches(_cachedGraph);
        CachedGraphPrototypeValidation.ValidateFixedDownstreamContinuation();
    }

    [Benchmark(Baseline = true, Description = "Current captured closure")]
    public Task<int> CurrentCapturedClosure()
    {
        return CachedGraphPrototypeDispatch.ExecuteCapturedClosureAsync(
            0,
            _request,
            _handler,
            _behaviors);
    }

    [Benchmark(Description = "Readonly struct continuation")]
    public Task<int> ReadonlyStructContinuation()
    {
        return new CachedGraphPrototypeStructContinuation(
            _request,
            _handler,
            _behaviors,
            0).InvokeAsync();
    }

    [Benchmark(Description = "Cached graph + per-dispatch state")]
    public Task<int> CachedGraphPerDispatchState()
    {
        var execution = new CachedGraphPrototypeExecution(_handler, _behaviors);
        return _cachedGraph.ExecuteAsync(execution, _request);
    }
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class UOCachedPipelineGraphPrototypeBenchmarksDi
{
    private const int ExpectedResponse = 42;

    private readonly CachedGraphPrototypeRequest _request = new(ExpectedResponse);

    private ServiceProvider _serviceProvider = null!;
    private CachedGraphPrototypePipeline _cachedGraph = null!;

    [Params(1, 3, 5)]
    public int BehaviorCount { get; set; }

    [Params(ServiceLifetime.Singleton, ServiceLifetime.Transient)]
    public ServiceLifetime BehaviorLifetime { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _serviceProvider = CreateServiceProvider(BehaviorCount, BehaviorLifetime);
        _cachedGraph = CachedGraphPrototypePipeline.Create(BehaviorCount);

        // Warm Microsoft DI call-sites and every continuation shape. Transient
        // instances created here are not stored or reused by a benchmark method.
        ValidateResponse(CurrentCapturedClosure(), "Current captured closure");
        ValidateResponse(ReadonlyStructContinuation(), "Readonly struct continuation");
        ValidateResponse(CachedGraphPerDispatchState(), "Cached graph + per-dispatch state");

        CachedGraphPrototypeValidation.ValidateScopedLifetimes();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "DI + current captured closure")]
    public Task<int> CurrentCapturedClosure()
    {
        var handler = _serviceProvider.GetRequiredService<ICachedGraphPrototypeHandler>();
        var behaviors = ResolveBehaviors(_serviceProvider);

        return CachedGraphPrototypeDispatch.ExecuteCapturedClosureAsync(
            0,
            _request,
            handler,
            behaviors);
    }

    [Benchmark(Description = "DI + readonly struct continuation")]
    public Task<int> ReadonlyStructContinuation()
    {
        var handler = _serviceProvider.GetRequiredService<ICachedGraphPrototypeHandler>();
        var behaviors = ResolveBehaviors(_serviceProvider);

        return new CachedGraphPrototypeStructContinuation(
            _request,
            handler,
            behaviors,
            0).InvokeAsync();
    }

    [Benchmark(Description = "DI + cached graph + per-dispatch state")]
    public Task<int> CachedGraphPerDispatchState()
    {
        var handler = _serviceProvider.GetRequiredService<ICachedGraphPrototypeHandler>();
        var behaviors = ResolveBehaviors(_serviceProvider);
        var execution = new CachedGraphPrototypeExecution(handler, behaviors);

        return _cachedGraph.ExecuteAsync(execution, _request);
    }

    private static ServiceProvider CreateServiceProvider(
        int behaviorCount,
        ServiceLifetime behaviorLifetime)
    {
        var services = new ServiceCollection();
        services.AddTransient<ICachedGraphPrototypeHandler, CachedGraphPrototypeHandler>();

        for (var position = 0; position < behaviorCount; position++)
        {
            if (behaviorLifetime == ServiceLifetime.Singleton)
            {
                services.AddSingleton<
                    ICachedGraphPrototypeBehavior,
                    CachedGraphPrototypeBehavior>();
            }
            else
            {
                services.AddTransient<
                    ICachedGraphPrototypeBehavior,
                    CachedGraphPrototypeBehavior>();
            }
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IReadOnlyList<ICachedGraphPrototypeBehavior> ResolveBehaviors(
        IServiceProvider serviceProvider)
    {
        var behaviors = serviceProvider.GetServices<ICachedGraphPrototypeBehavior>();
        return behaviors as IReadOnlyList<ICachedGraphPrototypeBehavior> ?? behaviors.ToArray();
    }

    private static void ValidateResponse(Task<int> responseTask, string architecture)
    {
        var response = responseTask.GetAwaiter().GetResult();

        if (response != ExpectedResponse)
        {
            throw new InvalidOperationException(
                $"{architecture} returned {response}; expected {ExpectedResponse}.");
        }
    }
}

internal static class CachedGraphPrototypeDispatch
{
    public static Task<int> ExecuteCapturedClosureAsync(
        int position,
        CachedGraphPrototypeRequest request,
        ICachedGraphPrototypeHandler handler,
        IReadOnlyList<ICachedGraphPrototypeBehavior> behaviors)
    {
        if (position == behaviors.Count)
        {
            request.Probe?.RecordHandler(handler);
            return handler.HandleAsync(request);
        }

        var behavior = behaviors[position];
        request.Probe?.RecordBehavior(position, behavior);

        return behavior.HandleAsync(
            request,
            () => ExecuteCapturedClosureAsync(position + 1, request, handler, behaviors));
    }
}

internal sealed record CachedGraphPrototypeRequest(
    int Value,
    CachedGraphPrototypeProbe? Probe = null);

internal interface ICachedGraphPrototypeHandler
{
    Task<int> HandleAsync(CachedGraphPrototypeRequest request);
}

internal sealed class CachedGraphPrototypeHandler : ICachedGraphPrototypeHandler
{
    public Task<int> HandleAsync(CachedGraphPrototypeRequest request)
    {
        return Task.FromResult(request.Value);
    }
}

internal interface ICachedGraphPrototypeBehavior
{
    Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        PrototypeRequestHandlerDelegate<int> next);

    Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        CachedGraphPrototypeStructContinuation next);

    Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        CachedGraphPrototypeNext next);
}

internal sealed class CachedGraphPrototypeBehavior : ICachedGraphPrototypeBehavior
{
    public Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        PrototypeRequestHandlerDelegate<int> next)
    {
        return next();
    }

    public Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        CachedGraphPrototypeStructContinuation next)
    {
        return next.InvokeAsync();
    }

    public Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        CachedGraphPrototypeNext next)
    {
        return next.InvokeAsync(request);
    }
}

internal sealed class CachedGraphPrototypeExecution
{
    public CachedGraphPrototypeExecution(
        ICachedGraphPrototypeHandler handler,
        IReadOnlyList<ICachedGraphPrototypeBehavior> behaviors)
    {
        Handler = handler;
        Behaviors = behaviors;
    }

    public ICachedGraphPrototypeHandler Handler { get; }

    public IReadOnlyList<ICachedGraphPrototypeBehavior> Behaviors { get; }
}

internal readonly struct CachedGraphPrototypeStructContinuation
{
    private readonly CachedGraphPrototypeRequest _request;
    private readonly ICachedGraphPrototypeHandler _handler;
    private readonly IReadOnlyList<ICachedGraphPrototypeBehavior> _behaviors;
    private readonly int _position;

    public CachedGraphPrototypeStructContinuation(
        CachedGraphPrototypeRequest request,
        ICachedGraphPrototypeHandler handler,
        IReadOnlyList<ICachedGraphPrototypeBehavior> behaviors,
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
            _request.Probe?.RecordHandler(_handler);
            return _handler.HandleAsync(_request);
        }

        var behavior = _behaviors[_position];
        _request.Probe?.RecordBehavior(_position, behavior);
        var next = new CachedGraphPrototypeStructContinuation(
            _request,
            _handler,
            _behaviors,
            _position + 1);

        return behavior.HandleAsync(_request, next);
    }
}

internal delegate Task<int> CachedGraphPrototypeStep(
    CachedGraphPrototypeExecution execution,
    CachedGraphPrototypeRequest request);

internal readonly struct CachedGraphPrototypeNext
{
    private readonly CachedGraphPrototypeExecution _execution;
    private readonly CachedGraphPrototypeStep _step;

    public CachedGraphPrototypeNext(
        CachedGraphPrototypeExecution execution,
        CachedGraphPrototypeStep step)
    {
        _execution = execution;
        _step = step;
    }

    public Task<int> InvokeAsync(CachedGraphPrototypeRequest request)
    {
        return _step(_execution, request);
    }
}

internal sealed class CachedGraphPrototypePipeline
{
    private readonly CachedGraphPrototypeStep _root;
    private readonly int _behaviorCount;

    private CachedGraphPrototypePipeline(CachedGraphPrototypeStep root, int behaviorCount)
    {
        _root = root;
        _behaviorCount = behaviorCount;
    }

    internal int BehaviorCount => _behaviorCount;

    public static CachedGraphPrototypePipeline Create(int behaviorCount)
    {
        CachedGraphPrototypeStep next = InvokeHandlerAsync;

        for (var position = behaviorCount - 1; position >= 0; position--)
        {
            var step = new CachedGraphPrototypeBehaviorStep(position, next);
            next = step.InvokeAsync;
        }

        return new CachedGraphPrototypePipeline(next, behaviorCount);
    }

    public Task<int> ExecuteAsync(
        CachedGraphPrototypeExecution execution,
        CachedGraphPrototypeRequest request)
    {
        if (execution.Behaviors.Count != _behaviorCount)
        {
            throw new InvalidOperationException(
                $"The cached graph expects {_behaviorCount} behaviors, but the dispatch resolved " +
                $"{execution.Behaviors.Count}.");
        }

        return _root(execution, request);
    }

    private static Task<int> InvokeHandlerAsync(
        CachedGraphPrototypeExecution execution,
        CachedGraphPrototypeRequest request)
    {
        request.Probe?.RecordHandler(execution.Handler);
        return execution.Handler.HandleAsync(request);
    }

    private sealed class CachedGraphPrototypeBehaviorStep
    {
        private readonly int _position;
        private readonly CachedGraphPrototypeStep _next;

        public CachedGraphPrototypeBehaviorStep(
            int position,
            CachedGraphPrototypeStep next)
        {
            _position = position;
            _next = next;
        }

        public Task<int> InvokeAsync(
            CachedGraphPrototypeExecution execution,
            CachedGraphPrototypeRequest request)
        {
            var behavior = execution.Behaviors[_position];
            request.Probe?.RecordBehavior(_position, behavior);
            var next = new CachedGraphPrototypeNext(execution, _next);

            return behavior.HandleAsync(request, next);
        }
    }
}

internal static class CachedGraphPrototypeValidation
{
    private const int ConcurrentDispatchCount = 32;

    public static void ValidateArchitectures(
        int behaviorCount,
        ICachedGraphPrototypeHandler handler,
        IReadOnlyList<ICachedGraphPrototypeBehavior> behaviors,
        CachedGraphPrototypePipeline cachedGraph)
    {
        ValidateArchitecture(
            "Current captured closure",
            behaviorCount,
            handler,
            behaviors,
            request => CachedGraphPrototypeDispatch.ExecuteCapturedClosureAsync(
                0,
                request,
                handler,
                behaviors));
        ValidateArchitecture(
            "Readonly struct continuation",
            behaviorCount,
            handler,
            behaviors,
            request => new CachedGraphPrototypeStructContinuation(
                request,
                handler,
                behaviors,
                0).InvokeAsync());
        ValidateArchitecture(
            "Cached graph + per-dispatch state",
            behaviorCount,
            handler,
            behaviors,
            request => cachedGraph.ExecuteAsync(
                new CachedGraphPrototypeExecution(handler, behaviors),
                request));
    }

    public static void ValidateConcurrentDispatches(CachedGraphPrototypePipeline cachedGraph)
    {
        using var startGate = new ManualResetEventSlim(initialState: false);
        var handlers = new ICachedGraphPrototypeHandler[ConcurrentDispatchCount];
        var behaviorSets = new ICachedGraphPrototypeBehavior[ConcurrentDispatchCount][];
        var requests = new CachedGraphPrototypeRequest[ConcurrentDispatchCount];
        var tasks = new Task[ConcurrentDispatchCount];

        for (var dispatchIndex = 0; dispatchIndex < ConcurrentDispatchCount; dispatchIndex++)
        {
            var currentDispatch = dispatchIndex;
            var handler = new CachedGraphPrototypeHandler();
            var behaviors = Enumerable.Range(0, cachedGraph.BehaviorCount)
                .Select(_ => (ICachedGraphPrototypeBehavior)new CachedGraphPrototypeBehavior())
                .ToArray();
            var probe = new CachedGraphPrototypeProbe(behaviors, handler);
            var request = new CachedGraphPrototypeRequest(1_000 + dispatchIndex, probe);

            handlers[dispatchIndex] = handler;
            behaviorSets[dispatchIndex] = behaviors;
            requests[dispatchIndex] = request;
            tasks[dispatchIndex] = Task.Run(async () =>
            {
                startGate.Wait();

                var execution = new CachedGraphPrototypeExecution(handler, behaviors);
                var response = await cachedGraph.ExecuteAsync(execution, request);

                if (response != request.Value)
                {
                    throw new InvalidOperationException(
                        $"Concurrent dispatch {currentDispatch} returned {response}; " +
                        $"expected {request.Value}.");
                }

                probe.AssertComplete(
                    $"Concurrent cached-graph dispatch {currentDispatch}",
                    cachedGraph.BehaviorCount);
            });
        }

        if (handlers.Distinct(ReferenceEqualityComparer.Instance).Count() != ConcurrentDispatchCount)
        {
            throw new InvalidOperationException("Concurrent dispatches did not use distinct handlers.");
        }

        if (requests.Distinct(ReferenceEqualityComparer.Instance).Count() != ConcurrentDispatchCount)
        {
            throw new InvalidOperationException("Concurrent dispatches did not use distinct requests.");
        }

        var allBehaviors = behaviorSets.SelectMany(static behaviors => behaviors).ToArray();

        if (allBehaviors.Distinct(ReferenceEqualityComparer.Instance).Count() != allBehaviors.Length)
        {
            throw new InvalidOperationException(
                "A behavior instance was shared between concurrent dispatch contexts.");
        }

        startGate.Set();
        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    public static void ValidateFixedDownstreamContinuation()
    {
        var graph = CachedGraphPrototypePipeline.Create(1);
        var handler = new CountingCachedGraphPrototypeHandler();
        ICachedGraphPrototypeBehavior[] behaviors = [new DoubleInvokeCachedGraphPrototypeBehavior()];
        var execution = new CachedGraphPrototypeExecution(handler, behaviors);
        var request = new CachedGraphPrototypeRequest(77);
        var response = graph.ExecuteAsync(execution, request).GetAwaiter().GetResult();

        if (response != request.Value || handler.InvocationCount != 2)
        {
            throw new InvalidOperationException(
                "Calling the same cached continuation twice did not restart at its fixed " +
                "downstream position.");
        }
    }

    public static void ValidateScopedLifetimes()
    {
        const int behaviorCount = 3;

        var services = new ServiceCollection();
        services.AddScoped<ICachedGraphPrototypeHandler, CachedGraphPrototypeHandler>();

        for (var position = 0; position < behaviorCount; position++)
        {
            services.AddScoped<ICachedGraphPrototypeBehavior, CachedGraphPrototypeBehavior>();
        }

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var graph = CachedGraphPrototypePipeline.Create(behaviorCount);

        ICachedGraphPrototypeHandler firstHandler;
        ICachedGraphPrototypeBehavior[] firstBehaviors;

        using (var firstScope = provider.CreateScope())
        {
            firstHandler = firstScope.ServiceProvider
                .GetRequiredService<ICachedGraphPrototypeHandler>();
            firstBehaviors = firstScope.ServiceProvider
                .GetServices<ICachedGraphPrototypeBehavior>()
                .ToArray();

            AssertScopedInstancesAreReusedWithinScope(
                firstScope.ServiceProvider,
                firstHandler,
                firstBehaviors);
            ValidateCachedExecution(
                "First scoped cached-graph dispatch",
                graph,
                firstHandler,
                firstBehaviors,
                201);
        }

        using (var secondScope = provider.CreateScope())
        {
            var secondHandler = secondScope.ServiceProvider
                .GetRequiredService<ICachedGraphPrototypeHandler>();
            var secondBehaviors = secondScope.ServiceProvider
                .GetServices<ICachedGraphPrototypeBehavior>()
                .ToArray();

            if (ReferenceEquals(firstHandler, secondHandler))
            {
                throw new InvalidOperationException("Scoped handler leaked across scopes.");
            }

            for (var position = 0; position < behaviorCount; position++)
            {
                if (ReferenceEquals(firstBehaviors[position], secondBehaviors[position]))
                {
                    throw new InvalidOperationException(
                        $"Scoped behavior at position {position} leaked across scopes.");
                }
            }

            AssertScopedInstancesAreReusedWithinScope(
                secondScope.ServiceProvider,
                secondHandler,
                secondBehaviors);
            ValidateCachedExecution(
                "Second scoped cached-graph dispatch",
                graph,
                secondHandler,
                secondBehaviors,
                202);
        }
    }

    private static void ValidateArchitecture(
        string architecture,
        int behaviorCount,
        ICachedGraphPrototypeHandler handler,
        IReadOnlyList<ICachedGraphPrototypeBehavior> behaviors,
        Func<CachedGraphPrototypeRequest, Task<int>> dispatchAsync)
    {
        var probe = new CachedGraphPrototypeProbe(behaviors, handler);
        var request = new CachedGraphPrototypeRequest(42, probe);
        var response = dispatchAsync(request).GetAwaiter().GetResult();

        if (response != request.Value)
        {
            throw new InvalidOperationException(
                $"{architecture} returned {response}; expected {request.Value}.");
        }

        probe.AssertComplete(architecture, behaviorCount);
    }

    private static void ValidateCachedExecution(
        string architecture,
        CachedGraphPrototypePipeline graph,
        ICachedGraphPrototypeHandler handler,
        IReadOnlyList<ICachedGraphPrototypeBehavior> behaviors,
        int requestValue)
    {
        var probe = new CachedGraphPrototypeProbe(behaviors, handler);
        var request = new CachedGraphPrototypeRequest(requestValue, probe);
        var execution = new CachedGraphPrototypeExecution(handler, behaviors);
        var response = graph.ExecuteAsync(execution, request).GetAwaiter().GetResult();

        if (response != requestValue)
        {
            throw new InvalidOperationException(
                $"{architecture} returned {response}; expected {requestValue}.");
        }

        probe.AssertComplete(architecture, behaviors.Count);
    }

    private static void AssertScopedInstancesAreReusedWithinScope(
        IServiceProvider serviceProvider,
        ICachedGraphPrototypeHandler expectedHandler,
        IReadOnlyList<ICachedGraphPrototypeBehavior> expectedBehaviors)
    {
        var resolvedHandler = serviceProvider.GetRequiredService<ICachedGraphPrototypeHandler>();
        var resolvedBehaviors = serviceProvider
            .GetServices<ICachedGraphPrototypeBehavior>()
            .ToArray();

        if (!ReferenceEquals(expectedHandler, resolvedHandler))
        {
            throw new InvalidOperationException(
                "Scoped handler was not reused within its own scope.");
        }

        for (var position = 0; position < expectedBehaviors.Count; position++)
        {
            if (!ReferenceEquals(expectedBehaviors[position], resolvedBehaviors[position]))
            {
                throw new InvalidOperationException(
                    $"Scoped behavior at position {position} was not reused within its scope.");
            }
        }
    }
}

internal sealed class CountingCachedGraphPrototypeHandler : ICachedGraphPrototypeHandler
{
    private int _invocationCount;

    public int InvocationCount => _invocationCount;

    public Task<int> HandleAsync(CachedGraphPrototypeRequest request)
    {
        Interlocked.Increment(ref _invocationCount);
        return Task.FromResult(request.Value);
    }
}

internal sealed class DoubleInvokeCachedGraphPrototypeBehavior : ICachedGraphPrototypeBehavior
{
    public Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        PrototypeRequestHandlerDelegate<int> next)
    {
        throw new NotSupportedException("This validation behavior is cached-graph-only.");
    }

    public Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        CachedGraphPrototypeStructContinuation next)
    {
        throw new NotSupportedException("This validation behavior is cached-graph-only.");
    }

    public async Task<int> HandleAsync(
        CachedGraphPrototypeRequest request,
        CachedGraphPrototypeNext next)
    {
        var responses = await Task.WhenAll(
            next.InvokeAsync(request),
            next.InvokeAsync(request));

        if (responses[0] != request.Value || responses[1] != request.Value)
        {
            throw new InvalidOperationException(
                "Repeated cached continuation calls returned different responses.");
        }

        return responses[0];
    }
}

internal sealed class CachedGraphPrototypeProbe
{
    private readonly IReadOnlyList<ICachedGraphPrototypeBehavior> _expectedBehaviors;
    private readonly ICachedGraphPrototypeHandler _expectedHandler;
    private readonly int[] _executionOrder;
    private int _eventCount;
    private int _handlerCount;

    public CachedGraphPrototypeProbe(
        IReadOnlyList<ICachedGraphPrototypeBehavior> expectedBehaviors,
        ICachedGraphPrototypeHandler expectedHandler)
    {
        _expectedBehaviors = expectedBehaviors;
        _expectedHandler = expectedHandler;
        _executionOrder = new int[expectedBehaviors.Count];
    }

    public void RecordBehavior(int position, ICachedGraphPrototypeBehavior behavior)
    {
        if (_eventCount >= _executionOrder.Length)
        {
            throw new InvalidOperationException("A behavior executed more than once.");
        }

        if (!ReferenceEquals(behavior, _expectedBehaviors[position]))
        {
            throw new InvalidOperationException(
                $"Dispatch used a behavior instance from another execution at position {position}.");
        }

        _executionOrder[_eventCount] = position;
        _eventCount++;
    }

    public void RecordHandler(ICachedGraphPrototypeHandler handler)
    {
        if (!ReferenceEquals(handler, _expectedHandler))
        {
            throw new InvalidOperationException(
                "Dispatch used a handler instance from another execution.");
        }

        _handlerCount++;
    }

    public void AssertComplete(string architecture, int behaviorCount)
    {
        if (_handlerCount != 1)
        {
            throw new InvalidOperationException(
                $"{architecture} executed the handler {_handlerCount} times.");
        }

        if (_eventCount != behaviorCount)
        {
            throw new InvalidOperationException(
                $"{architecture} executed {_eventCount} of {behaviorCount} behaviors.");
        }

        for (var position = 0; position < behaviorCount; position++)
        {
            if (_executionOrder[position] != position)
            {
                throw new InvalidOperationException(
                    $"{architecture} executed behavior {_executionOrder[position]} at position {position}.");
            }
        }
    }
}

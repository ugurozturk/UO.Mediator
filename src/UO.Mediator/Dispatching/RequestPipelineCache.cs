using System.Collections.Concurrent;

namespace UO.Mediator.Dispatching;

internal sealed class RequestPipelineCache
{
    private readonly ConcurrentDictionary<PipelineCacheKey, Lazy<object>> _pipelines = new();

    public RequestPipeline<TRequest, TResponse, IRequestHandler<TRequest, TResponse>>
        GetOrAdd<TRequest, TResponse>(
            IReadOnlyList<IRequestBehavior<TRequest, TResponse>> behaviors)
        where TRequest : IRequest<TResponse>
    {
        var key = new PipelineCacheKey(typeof(TRequest), typeof(TResponse), HasResponse: true);
        var pipeline = _pipelines.GetOrAdd(
            key,
            _ => new Lazy<object>(
                () => RequestPipeline<
                    TRequest,
                    TResponse,
                    IRequestHandler<TRequest, TResponse>>.Create(
                        behaviors,
                        static (request, handler) => handler.HandleAsync(request)),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return (RequestPipeline<TRequest, TResponse, IRequestHandler<TRequest, TResponse>>)pipeline.Value;
    }

    public RequestPipeline<TRequest, Unit, IRequestHandler<TRequest>> GetOrAddNoResponse<TRequest>(
        IReadOnlyList<IRequestBehavior<TRequest, Unit>> behaviors)
        where TRequest : IRequest
    {
        var key = new PipelineCacheKey(typeof(TRequest), typeof(Unit), HasResponse: false);
        var pipeline = _pipelines.GetOrAdd(
            key,
            _ => new Lazy<object>(
                () => RequestPipeline<TRequest, Unit, IRequestHandler<TRequest>>.Create(
                    behaviors,
                    static async (request, handler) =>
                    {
                        await handler.HandleAsync(request);
                        return Unit.Value;
                    }),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return (RequestPipeline<TRequest, Unit, IRequestHandler<TRequest>>)pipeline.Value;
    }

    private readonly record struct PipelineCacheKey(
        Type RequestType,
        Type ResponseType,
        bool HasResponse);
}

internal sealed class RequestPipeline<TRequest, TResponse, THandler>
    : IRequestPipelineInvoker<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly PipelineBehavior[] _behaviors;
    private readonly Func<TRequest, THandler, Task<TResponse>> _handlerInvoker;

    private RequestPipeline(
        PipelineBehavior[] behaviors,
        Func<TRequest, THandler, Task<TResponse>> handlerInvoker)
    {
        _behaviors = behaviors;
        _handlerInvoker = handlerInvoker;
    }

    public static RequestPipeline<TRequest, TResponse, THandler> Create(
        IReadOnlyList<IRequestBehavior<TRequest, TResponse>> behaviors,
        Func<TRequest, THandler, Task<TResponse>> handlerInvoker)
    {
        var pipelineBehaviors = behaviors
            .Select((behavior, index) => new
            {
                Type = behavior.GetType(),
                behavior.Order,
                ServiceIndex = index
            })
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Type.FullName, StringComparer.Ordinal)
            .Select(item => new PipelineBehavior(item.Type, item.ServiceIndex))
            .ToArray();

        return new RequestPipeline<TRequest, TResponse, THandler>(pipelineBehaviors, handlerInvoker);
    }

    public Task<TResponse> ExecuteAsync(
        TRequest request,
        THandler handler,
        IReadOnlyList<IRequestBehavior<TRequest, TResponse>> behaviors)
    {
        return ExecuteBehaviorAsync(0, request, handler, behaviors);
    }

    private Task<TResponse> ExecuteBehaviorAsync(
        int position,
        TRequest request,
        THandler handler,
        IReadOnlyList<IRequestBehavior<TRequest, TResponse>> behaviors)
    {
        if (position == _behaviors.Length)
        {
            return _handlerInvoker(request, handler);
        }

        var behavior = behaviors[_behaviors[position].ServiceIndex];
        var next = new RequestHandlerNext<TRequest, TResponse>(
            this,
            request,
            handler!,
            behaviors,
            position + 1);

        return behavior.HandleAsync(
            request,
            next);
    }

    Task<TResponse> IRequestPipelineInvoker<TRequest, TResponse>.InvokeAsync(
        int position,
        TRequest request,
        object handler,
        IReadOnlyList<IRequestBehavior<TRequest, TResponse>> behaviors)
    {
        return ExecuteBehaviorAsync(position, request, (THandler)handler, behaviors);
    }

    private readonly record struct PipelineBehavior(Type BehaviorType, int ServiceIndex);
}

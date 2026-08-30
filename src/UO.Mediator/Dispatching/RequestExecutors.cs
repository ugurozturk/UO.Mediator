namespace UO.Mediator.Dispatching;

internal interface IGeneratedRequestDescriptor
{
    Type RequestType { get; }

    Type ResponseType { get; }

    bool HasResponse { get; }

    Type HandlerContract { get; }
}

internal sealed class GeneratedRequestDescriptor<TRequest, TResponse>
    : IGeneratedRequestDescriptor
    where TRequest : IRequest<TResponse>
{
    public GeneratedRequestDescriptor()
    {
    }

    public Type RequestType => typeof(TRequest);

    public Type ResponseType => typeof(TResponse);

    public bool HasResponse => true;

    public Type HandlerContract => typeof(IRequestHandler<TRequest, TResponse>);
}

internal sealed class GeneratedNoResponseRequestDescriptor<TRequest>
    : IGeneratedRequestDescriptor
    where TRequest : IRequest
{
    public GeneratedNoResponseRequestDescriptor()
    {
    }

    public Type RequestType => typeof(TRequest);

    public Type ResponseType => typeof(Unit);

    public bool HasResponse => false;

    public Type HandlerContract => typeof(IRequestHandler<TRequest>);
}

internal interface IRequestExecutor
{
    Type RequestType { get; }

    Type ResponseType { get; }

    Type HandlerContract { get; }
}

internal interface IRequestExecutor<TResponse> : IRequestExecutor
{
    Task<TResponse> ExecuteAsync(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        RequestPipelineCache pipelineCache);
}

internal interface INoResponseRequestExecutor : IRequestExecutor
{
    Task ExecuteAsync(
        IRequest request,
        IServiceProvider serviceProvider,
        RequestPipelineCache pipelineCache);
}

internal sealed class NoResponseRequestExecutor<TRequest> : INoResponseRequestExecutor
    where TRequest : IRequest
{
    public Type RequestType => typeof(TRequest);

    public Type ResponseType => typeof(void);

    public Type HandlerContract => typeof(IRequestHandler<TRequest>);

    public Task ExecuteAsync(
        IRequest request,
        IServiceProvider serviceProvider,
        RequestPipelineCache pipelineCache)
    {
        return RequestExecution.ExecuteNoResponseAsync(
            (TRequest)request,
            serviceProvider,
            pipelineCache);
    }
}

internal sealed class RequestExecutor<TRequest, TResponse> : IRequestExecutor<TResponse>
    where TRequest : IRequest<TResponse>
{
    public Type RequestType => typeof(TRequest);

    public Type ResponseType => typeof(TResponse);

    public Type HandlerContract => typeof(IRequestHandler<TRequest, TResponse>);

    public Task<TResponse> ExecuteAsync(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        RequestPipelineCache pipelineCache)
    {
        return RequestExecution.ExecuteAsync<TRequest, TResponse>(
            (TRequest)request,
            serviceProvider,
            pipelineCache);
    }
}

internal sealed class RequestExecutorRegistry(IEnumerable<IRequestExecutor> generatedExecutors)
{
    private readonly Dictionary<(Type Request, Type Response), IRequestExecutor>
        _generatedExecutors = generatedExecutors
            .GroupBy(executor => (executor.RequestType, executor.ResponseType))
            .ToDictionary(group => group.Key, group => group.First());

    public IEnumerable<IRequestExecutor> GeneratedExecutors =>
        _generatedExecutors.Values;

    public IRequestExecutor Get(Type requestType, Type responseType)
    {
        if (_generatedExecutors.TryGetValue(
                (requestType, responseType),
                out var executor))
        {
            return executor;
        }

        throw CreateMissingExecutorException(requestType, responseType);
    }

    public IRequestExecutor GetNoResponse(Type requestType)
    {
        if (_generatedExecutors.TryGetValue(
                (requestType, typeof(void)),
                out var executor))
        {
            return executor;
        }

        throw CreateMissingExecutorException(requestType, typeof(void));
    }

    private static InvalidOperationException CreateMissingExecutorException(
        Type requestType,
        Type responseType)
    {
        return new InvalidOperationException(
            $"No source-generated executor is registered for request '{requestType.FullName}' " +
            $"and response '{responseType.FullName}'. Call the generated UO.Mediator " +
            "registration method for the assembly that owns this handler.");
    }
}

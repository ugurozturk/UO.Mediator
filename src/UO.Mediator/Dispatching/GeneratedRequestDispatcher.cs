using Microsoft.Extensions.DependencyInjection;

namespace UO.Mediator.Dispatching;

internal sealed class GeneratedRequestDispatcher(IServiceProvider serviceProvider)
    : IRequestDispatcher
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly RequestPipelineCache _pipelineCache =
        serviceProvider.GetRequiredService<RequestPipelineCache>();
    private readonly RequestExecutorRegistry _executorRegistry =
        serviceProvider.GetRequiredService<RequestExecutorRegistry>();

    public Task DispatchAsync(IRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var executor = _executorRegistry.GetNoResponse(request.GetType());

        return ((INoResponseRequestExecutor)executor).ExecuteAsync(
            request,
            _serviceProvider,
            _pipelineCache);
    }

    public Task<TResponse> DispatchAsync<TResponse>(IRequest<TResponse> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(request);
    }

    private Task<TResponse> ExecuteAsync<TResponse>(IRequest<TResponse> request)
    {
        var executor = _executorRegistry.Get(
            request.GetType(),
            typeof(TResponse));

        return ((IRequestExecutor<TResponse>)executor).ExecuteAsync(
            request,
            _serviceProvider,
            _pipelineCache);
    }
}

internal sealed class GeneratedRoutingRequestDispatcher(IServiceProvider serviceProvider)
    : IRequestDispatcher, IGeneratedDispatchContext
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly RequestPipelineCache _pipelineCache =
        serviceProvider.GetRequiredService<RequestPipelineCache>();
    private RequestExecutorRegistry? _executorRegistry;

    public Task DispatchAsync(IRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request is IGeneratedRequestRoute generatedRoute)
        {
            return generatedRoute.DispatchAsync(this);
        }

        var executor = GetExecutorRegistry().GetNoResponse(request.GetType());
        return ((INoResponseRequestExecutor)executor).ExecuteAsync(
            request,
            _serviceProvider,
            _pipelineCache);
    }

    public Task<TResponse> DispatchAsync<TResponse>(IRequest<TResponse> request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request is IGeneratedRequestRoute<TResponse> generatedRoute)
        {
            return generatedRoute.DispatchAsync(this);
        }

        var executor = GetExecutorRegistry().Get(request.GetType(), typeof(TResponse));
        return ((IRequestExecutor<TResponse>)executor).ExecuteAsync(
            request,
            _serviceProvider,
            _pipelineCache);
    }

    Task IGeneratedDispatchContext.DispatchAsync<TRequest>(TRequest request)
    {
        return RequestExecution.ExecuteNoResponseAsync(
            request,
            _serviceProvider,
            _pipelineCache);
    }

    Task<TResponse> IGeneratedDispatchContext.DispatchAsync<TRequest, TResponse>(
        TRequest request)
    {
        return RequestExecution.ExecuteAsync<TRequest, TResponse>(
            request,
            _serviceProvider,
            _pipelineCache);
    }

    private RequestExecutorRegistry GetExecutorRegistry()
    {
        return _executorRegistry ??=
            _serviceProvider.GetRequiredService<RequestExecutorRegistry>();
    }
}

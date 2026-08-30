using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace UO.Mediator.Dispatching;

/// <summary>
/// Reflection-compatible implementation of <see cref="IRequestDispatcher"/>.
/// Caches closed-generic executors globally.
/// Prepared behaviour pipelines remain cached per service provider.
/// </summary>
public class RequestDispatcher(IServiceProvider serviceProvider) : IRequestDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), IRequestExecutor>
        Executors = new();
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly RequestPipelineCache _pipelineCache =
        serviceProvider.GetService<RequestPipelineCache>() ?? new RequestPipelineCache();

    /// <inheritdoc />
    public Task DispatchAsync(IRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestType = request.GetType();
        var executor = Executors.GetOrAdd(
            (requestType, typeof(void)),
            static key => CreateNoResponseExecutor(key.Request));

        return ((INoResponseRequestExecutor)executor).ExecuteAsync(
            request,
            _serviceProvider,
            _pipelineCache);
    }

    /// <inheritdoc />
    public Task<TResponse> DispatchAsync<TResponse>(IRequest<TResponse> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(request);
    }

    private Task<TResponse> ExecuteAsync<TResponse>(IRequest<TResponse> request)
    {
        var requestType = request.GetType();
        var executor = Executors.GetOrAdd(
            (requestType, typeof(TResponse)),
            static key => CreateExecutor(key.Request, key.Response));

        return ((IRequestExecutor<TResponse>)executor).ExecuteAsync(
            request,
            _serviceProvider,
            _pipelineCache);
    }

    private static IRequestExecutor CreateExecutor(Type requestType, Type responseType)
    {
        var executorType = typeof(RequestExecutor<,>).MakeGenericType(requestType, responseType);
        return (IRequestExecutor)Activator.CreateInstance(executorType)!;
    }

    private static IRequestExecutor CreateNoResponseExecutor(Type requestType)
    {
        var executorType = typeof(NoResponseRequestExecutor<>).MakeGenericType(requestType);
        return (IRequestExecutor)Activator.CreateInstance(executorType)!;
    }
}

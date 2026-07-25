using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace UO.Mediator.Dispatching;

/// <summary>
/// Default implementation of <see cref="IRequestDispatcher"/>.
/// Caches closed-generic executors per (request, response) pair and builds the behaviour chain once.
/// </summary>
public class RequestDispatcher(IServiceProvider serviceProvider) : IRequestDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), IRequestExecutor> Executors = new();
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <inheritdoc />
    public Task DispatchAsync(IRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestType = request.GetType();
        var executor = Executors.GetOrAdd(
            (requestType, typeof(void)),
            static key => CreateNoResponseExecutor(key.Request));

        return ((INoResponseRequestExecutor)executor).ExecuteAsync(request, _serviceProvider);
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

        return ((IRequestExecutor<TResponse>)executor).ExecuteAsync(request, _serviceProvider);
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

    private interface IRequestExecutor
    {
    }

    private interface IRequestExecutor<TResponse> : IRequestExecutor
    {
        Task<TResponse> ExecuteAsync(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider);
    }

    private interface INoResponseRequestExecutor : IRequestExecutor
    {
        Task ExecuteAsync(
            IRequest request,
            IServiceProvider serviceProvider);
    }

    private sealed class NoResponseRequestExecutor<TRequest> : INoResponseRequestExecutor
        where TRequest : IRequest
    {
        public async Task ExecuteAsync(
            IRequest request,
            IServiceProvider serviceProvider)
        {
            var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest>>();
            var behaviors = serviceProvider
                .GetServices<IRequestBehavior<TRequest, Unit>>()
                .OrderBy(x => x.Order)
                .ThenBy(x => x.GetType().FullName, StringComparer.Ordinal)
                .ToArray();

            RequestHandlerDelegate<Unit> next = async () =>
            {
                await handler.HandleAsync((TRequest)request);
                return Unit.Value;
            };

            for (var index = behaviors.Length - 1; index >= 0; index--)
            {
                var behavior = behaviors[index];
                var capturedNext = next;
                next = () => behavior.HandleAsync((TRequest)request, capturedNext);
            }

            await next();
        }
    }

    private sealed class RequestExecutor<TRequest, TResponse> : IRequestExecutor<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> ExecuteAsync(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider)
        {
            var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
            var behaviors = serviceProvider
                .GetServices<IRequestBehavior<TRequest, TResponse>>()
                .OrderBy(x => x.Order)
                .ThenBy(x => x.GetType().FullName, StringComparer.Ordinal)
                .ToArray();

            RequestHandlerDelegate<TResponse> next = () => handler.HandleAsync((TRequest)request);

            for (var index = behaviors.Length - 1; index >= 0; index--)
            {
                var behavior = behaviors[index];
                var capturedNext = next;
                next = () => behavior.HandleAsync((TRequest)request, capturedNext);
            }

            return next();
        }
    }
}

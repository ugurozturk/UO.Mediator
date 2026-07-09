using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Uozturk.Mediator.Dispatching;

/// <summary>
/// Default implementation of <see cref="IRequestDispatcher"/>.
/// Caches closed-generic executors per (request, response) pair and builds the behaviour chain once.
/// </summary>
public class RequestDispatcher(
    IServiceProvider serviceProvider,
    IRequestCancellationTokenProvider cancellationTokenProvider) : IRequestDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), IRequestExecutor> Executors = new();
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <inheritdoc />
    public async Task DispatchAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken = cancellationTokenProvider.GetCancellationToken(cancellationToken);
        var requestType = request.GetType();
        var executor = Executors.GetOrAdd(
            (requestType, typeof(void)),
            static key => CreateNoResponseExecutor(key.Request));

        using (cancellationTokenProvider.Use(cancellationToken))
        {
            await ((INoResponseRequestExecutor)executor).ExecuteAsync(
                request,
                _serviceProvider,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<TResponse> DispatchAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken = cancellationTokenProvider.GetCancellationToken(cancellationToken);
        using (cancellationTokenProvider.Use(cancellationToken))
        {
            return await ExecuteAsync(request, cancellationToken);
        }
    }

    private Task<TResponse> ExecuteAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();
        var executor = Executors.GetOrAdd(
            (requestType, typeof(TResponse)),
            static key => CreateExecutor(key.Request, key.Response));

        return ((IRequestExecutor<TResponse>)executor).ExecuteAsync(
            request,
            _serviceProvider,
            cancellationToken);
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
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken);
    }

    private interface INoResponseRequestExecutor : IRequestExecutor
    {
        Task ExecuteAsync(
            IRequest request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken);
    }

    private sealed class NoResponseRequestExecutor<TRequest> : INoResponseRequestExecutor
        where TRequest : IRequest
    {
        public async Task ExecuteAsync(
            IRequest request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest>>();
            var behaviors = serviceProvider
                .GetServices<IRequestBehavior<TRequest, Unit>>()
                .OrderBy(x => x.Order)
                .ThenBy(x => x.GetType().FullName, StringComparer.Ordinal)
                .ToArray();

            RequestHandlerDelegate<Unit> next = async () =>
            {
                await handler.HandleAsync((TRequest)request, cancellationToken);
                return Unit.Value;
            };

            for (var index = behaviors.Length - 1; index >= 0; index--)
            {
                var behavior = behaviors[index];
                var capturedNext = next;
                next = () => behavior.HandleAsync((TRequest)request, capturedNext, cancellationToken);
            }

            await next();
        }
    }

    private sealed class RequestExecutor<TRequest, TResponse> : IRequestExecutor<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> ExecuteAsync(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
            var behaviors = serviceProvider
                .GetServices<IRequestBehavior<TRequest, TResponse>>()
                .OrderBy(x => x.Order)
                .ThenBy(x => x.GetType().FullName, StringComparer.Ordinal)
                .ToArray();

            RequestHandlerDelegate<TResponse> next = () =>
                handler.HandleAsync((TRequest)request, cancellationToken);

            for (var index = behaviors.Length - 1; index >= 0; index--)
            {
                var behavior = behaviors[index];
                var capturedNext = next;
                next = () => behavior.HandleAsync((TRequest)request, capturedNext, cancellationToken);
            }

            return next();
        }
    }
}

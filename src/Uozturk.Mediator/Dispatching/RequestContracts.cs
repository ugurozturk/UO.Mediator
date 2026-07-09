using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

namespace Uozturk.Mediator.Dispatching;

/// <summary>
/// Unit type used as a placeholder response for requests that do not return a value.
/// </summary>
public readonly record struct Unit
{
    /// <summary>
    /// The singleton unit value.
    /// </summary>
    public static Unit Value { get; } = new();
}

/// <summary>
/// Marker interface for a request that does not return a value.
/// </summary>
public interface IRequest : IRequest<Unit>
{
}

/// <summary>
/// Marker interface for a request that returns a value of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequest<out TResponse>
{
}

/// <summary>
/// Handler for a request that returns a value of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the request and returns a response.
    /// </summary>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler for a request that does not return a value.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest
{
    /// <summary>
    /// Handles the request.
    /// </summary>
    new Task HandleAsync(TRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicit interface implementation that adapts the void handler to the generic pipeline.
    /// </summary>
    async Task<Unit> IRequestHandler<TRequest, Unit>.HandleAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        await HandleAsync(request, cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// Delegate that represents the next stage in the dispatch pipeline.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting concern that wraps a request dispatch.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Deterministic ordering. Lower values run earlier in the pipeline.
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Invokes the behaviour around <paramref name="next"/>.
    /// </summary>
    Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Entry point for dispatching requests to their handlers through the behaviour pipeline.
/// </summary>
public interface IRequestDispatcher
{
    /// <summary>
    /// Dispatches a request that does not return a value.
    /// </summary>
    Task DispatchAsync(IRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a request that returns a value of type <typeparamref name="TResponse"/>.
    /// </summary>
    Task<TResponse> DispatchAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for the request dispatcher.
/// </summary>
public class RequestDispatcherOptions
{
    /// <summary>
    /// Assemblies scanned for request handlers, behaviours and startup validation.
    /// </summary>
    public IList<Assembly> Assemblies { get; } = [];

    /// <summary>
    /// Requests that take longer than this threshold are logged as warnings.
    /// Defaults to 1 second.
    /// </summary>
    public TimeSpan SlowRequestThreshold { get; set; } = TimeSpan.FromSeconds(1);
}

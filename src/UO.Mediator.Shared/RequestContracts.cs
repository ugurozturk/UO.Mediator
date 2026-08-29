using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("UO.Mediator")]

namespace UO.Mediator.Dispatching;

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
    Task<TResponse> HandleAsync(TRequest request);
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
    new Task HandleAsync(TRequest request);

    /// <summary>
    /// Explicit interface implementation that adapts the void handler to the generic pipeline.
    /// </summary>
    async Task<Unit> IRequestHandler<TRequest, Unit>.HandleAsync(TRequest request)
    {
        await HandleAsync(request);
        return Unit.Value;
    }
}

/// <summary>
/// Immutable continuation for the next stage in a request behavior pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// UO.Mediator creates this value for each downstream pipeline position. Calling
/// <see cref="InvokeAsync"/> more than once starts execution from that same position
/// each time.
/// </remarks>
public readonly struct RequestHandlerNext<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IRequestPipelineInvoker<TRequest, TResponse>? _pipeline;
    private readonly TRequest _request;
    private readonly object? _handler;
    private readonly IReadOnlyList<IRequestBehavior<TRequest, TResponse>>? _behaviors;
    private readonly int _position;

    internal RequestHandlerNext(
        IRequestPipelineInvoker<TRequest, TResponse> pipeline,
        TRequest request,
        object handler,
        IReadOnlyList<IRequestBehavior<TRequest, TResponse>> behaviors,
        int position)
    {
        _pipeline = pipeline;
        _request = request;
        _handler = handler;
        _behaviors = behaviors;
        _position = position;
    }

    /// <summary>
    /// Executes the downstream pipeline from this continuation's fixed position.
    /// </summary>
    public Task<TResponse> InvokeAsync()
    {
        if (_pipeline is null || _handler is null || _behaviors is null)
        {
            throw new InvalidOperationException(
                "The continuation must be created by UO.Mediator before it can be invoked.");
        }

        return _pipeline.InvokeAsync(
            _position,
            _request,
            _handler,
            _behaviors);
    }
}

internal interface IRequestPipelineInvoker<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> InvokeAsync(
        int position,
        TRequest request,
        object handler,
        IReadOnlyList<IRequestBehavior<TRequest, TResponse>> behaviors);
}

/// <summary>
/// Cross-cutting concern that wraps a request dispatch.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestBehavior<TRequest, TResponse>
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
        RequestHandlerNext<TRequest, TResponse> next);
}

/// <summary>
/// Entry point for dispatching requests to their handlers through the behaviour pipeline.
/// </summary>
public interface IRequestDispatcher
{
    /// <summary>
    /// Dispatches a request that does not return a value.
    /// </summary>
    Task DispatchAsync(IRequest request);

    /// <summary>
    /// Dispatches a request that returns a value of type <typeparamref name="TResponse"/>.
    /// </summary>
    Task<TResponse> DispatchAsync<TResponse>(IRequest<TResponse> request);
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

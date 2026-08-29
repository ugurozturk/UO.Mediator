using UO.Mediator.Dispatching;

namespace UO.Mediator.Benchmarks.Cases;

public sealed record BehaviorPipelineRequest(int Value) : IRequest<int>;

public sealed class BehaviorPipelineRequestHandler : IRequestHandler<BehaviorPipelineRequest, int>
{
    public Task<int> HandleAsync(BehaviorPipelineRequest request)
    {
        return Task.FromResult(request.Value + 1);
    }
}

public sealed class EmptyBehavior<TRequest, TResponse> : IRequestBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerNext<TRequest, TResponse> next)
    {
        return next.InvokeAsync();
    }
}

public sealed record SyncResponseRequest(int Value) : IRequest<int>;

public sealed class SyncResponseRequestHandler : IRequestHandler<SyncResponseRequest, int>
{
    public Task<int> HandleAsync(SyncResponseRequest request)
    {
        return Task.FromResult(request.Value + 1);
    }
}

public sealed record SyncCommand : IRequest;

public sealed class SyncCommandHandler : IRequestHandler<SyncCommand>
{
    public Task HandleAsync(SyncCommand request)
    {
        return Task.CompletedTask;
    }
}

public sealed record YieldResponseRequest(int Value) : IRequest<int>;

public sealed class YieldResponseRequestHandler : IRequestHandler<YieldResponseRequest, int>
{
    public async Task<int> HandleAsync(YieldResponseRequest request)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}

public sealed record YieldCommand : IRequest;

public sealed class YieldCommandHandler : IRequestHandler<YieldCommand>
{
    public async Task HandleAsync(YieldCommand request)
    {
        await Task.Yield();
    }
}

public sealed class LookupRequest<TMarker> : IRequest<int>
{
}

public sealed class LookupRequestHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request)
    {
        return Task.FromResult(default(TResponse)!);
    }
}

public sealed class LookupMarker<TFirst, TSecond, TThird>
{
}

public static class LookupRequestFactory
{
    private static readonly Type[] MarkerComponents =
    [
        typeof(byte),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(char),
        typeof(string),
        typeof(Guid)
    ];

    public static IReadOnlyList<IRequest<int>> Create(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            count,
            MarkerComponents.Length * MarkerComponents.Length * MarkerComponents.Length);

        var requests = new List<IRequest<int>>(count);

        foreach (var first in MarkerComponents)
        {
            foreach (var second in MarkerComponents)
            {
                foreach (var third in MarkerComponents)
                {
                    var markerType = typeof(LookupMarker<,,>).MakeGenericType(first, second, third);
                    var requestType = typeof(LookupRequest<>).MakeGenericType(markerType);
                    requests.Add((IRequest<int>)Activator.CreateInstance(requestType)!);

                    if (requests.Count == count)
                    {
                        return requests;
                    }
                }
            }
        }

        return requests;
    }
}

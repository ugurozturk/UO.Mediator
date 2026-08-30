using Microsoft.Extensions.DependencyInjection;

namespace UO.Mediator.Dispatching;

internal static class RequestExecution
{
    public static Task ExecuteNoResponseAsync<TRequest>(
        TRequest request,
        IServiceProvider serviceProvider,
        RequestPipelineCache pipelineCache)
        where TRequest : IRequest
    {
        var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest>>();
        var behaviors = ResolveBehaviors<IRequestBehavior<TRequest, Unit>>(serviceProvider);

        if (behaviors.Count == 0)
        {
            return handler.HandleAsync(request);
        }

        var pipeline = pipelineCache.GetOrAddNoResponse<TRequest>(behaviors);
        return pipeline.ExecuteAsync(request, handler, behaviors);
    }

    public static Task<TResponse> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        IServiceProvider serviceProvider,
        RequestPipelineCache pipelineCache)
        where TRequest : IRequest<TResponse>
    {
        var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        var behaviors = ResolveBehaviors<IRequestBehavior<TRequest, TResponse>>(serviceProvider);
        var pipeline = pipelineCache.GetOrAdd<TRequest, TResponse>(behaviors);

        return pipeline.ExecuteAsync(request, handler, behaviors);
    }

    private static IReadOnlyList<TBehavior> ResolveBehaviors<TBehavior>(
        IServiceProvider serviceProvider)
    {
        var behaviors = serviceProvider.GetServices<TBehavior>();
        return behaviors as IReadOnlyList<TBehavior> ?? behaviors.ToArray();
    }
}

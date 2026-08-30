using UO.Mediator.Dispatching;
using UO.Mediator.MultiAssembly.Contracts;

namespace UO.Mediator.MultiAssembly.HandlersA;

public sealed class CalculateHandler(MultiAssemblyTrace trace)
    : IRequestHandler<CalculateRequest, int>
{
    public Task<int> HandleAsync(CalculateRequest request)
    {
        trace.Events.Add("handler-a");
        return Task.FromResult(request.Value * 2);
    }
}

public sealed class CalculateBehavior(MultiAssemblyTrace trace)
    : IRequestBehavior<CalculateRequest, int>
{
    public async Task<int> HandleAsync(
        CalculateRequest request,
        RequestHandlerNext<CalculateRequest, int> next)
    {
        trace.Events.Add("behavior-a-before");
        var result = await next.InvokeAsync();
        trace.Events.Add("behavior-a-after");
        return result;
    }
}

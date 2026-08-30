using UO.Mediator.Dispatching;

namespace UO.Mediator.Benchmarks.Cases;

public sealed partial record UOPingRequest(int Value) : IRequest<int>;

public sealed class UOPingRequestHandler : IRequestHandler<UOPingRequest, int>
{
    public Task<int> HandleAsync(UOPingRequest request)
    {
        return Task.FromResult(request.Value + 1);
    }
}

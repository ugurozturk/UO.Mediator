using MediatR;

namespace UO.Mediator.Benchmarks.Cases;

public sealed record MediatRPingRequest(int Value) : IRequest<int>;

public sealed class MediatRPingRequestHandler : IRequestHandler<MediatRPingRequest, int>
{
    public Task<int> Handle(
        MediatRPingRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(request.Value + 1);
    }
}

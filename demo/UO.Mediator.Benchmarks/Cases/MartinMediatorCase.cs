using Mediator;

namespace UO.Mediator.Benchmarks.Cases;

public sealed record MartinPingRequest(int Value) : IRequest<int>;

public sealed class MartinPingRequestHandler : IRequestHandler<MartinPingRequest, int>
{
    public ValueTask<int> Handle(
        MartinPingRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<int>(request.Value + 1);
    }
}

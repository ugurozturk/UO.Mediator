using UO.Mediator.Dispatching;

namespace UO.Mediator.MultiAssembly.Contracts;

public sealed partial record CalculateRequest(int Value) : IRequest<int>;

public sealed partial record AuditCommand(string Message) : IRequest;

public sealed class MultiAssemblyTrace
{
    public List<string> Events { get; } = [];
}

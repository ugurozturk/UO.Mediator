using UO.Mediator.Dispatching;
using UO.Mediator.MultiAssembly.Contracts;

namespace UO.Mediator.MultiAssembly.HandlersB;

public sealed class AuditHandler(MultiAssemblyTrace trace)
    : IRequestHandler<AuditCommand>
{
    public Task HandleAsync(AuditCommand request)
    {
        trace.Events.Add(request.Message);
        return Task.CompletedTask;
    }
}

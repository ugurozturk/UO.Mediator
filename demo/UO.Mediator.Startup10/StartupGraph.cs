using UO.Mediator.Dispatching;

namespace UO.Mediator.Startup10;

public abstract record StartupRequestBase(int Value) : IRequest<int>;

public abstract class StartupHandler<TRequest> : IRequestHandler<TRequest, int>
    where TRequest : StartupRequestBase
{
    public Task<int> HandleAsync(TRequest request) => Task.FromResult(request.Value + 1);
}

public sealed partial record Request0001(int Value) : StartupRequestBase(Value);
public sealed class Handler0001 : StartupHandler<Request0001>;
public sealed partial record Request0002(int Value) : StartupRequestBase(Value);
public sealed class Handler0002 : StartupHandler<Request0002>;
public sealed partial record Request0003(int Value) : StartupRequestBase(Value);
public sealed class Handler0003 : StartupHandler<Request0003>;
public sealed partial record Request0004(int Value) : StartupRequestBase(Value);
public sealed class Handler0004 : StartupHandler<Request0004>;
public sealed partial record Request0005(int Value) : StartupRequestBase(Value);
public sealed class Handler0005 : StartupHandler<Request0005>;
public sealed partial record Request0006(int Value) : StartupRequestBase(Value);
public sealed class Handler0006 : StartupHandler<Request0006>;
public sealed partial record Request0007(int Value) : StartupRequestBase(Value);
public sealed class Handler0007 : StartupHandler<Request0007>;
public sealed partial record Request0008(int Value) : StartupRequestBase(Value);
public sealed class Handler0008 : StartupHandler<Request0008>;
public sealed partial record Request0009(int Value) : StartupRequestBase(Value);
public sealed class Handler0009 : StartupHandler<Request0009>;
public sealed partial record Request0010(int Value) : StartupRequestBase(Value);
public sealed class Handler0010 : StartupHandler<Request0010>;

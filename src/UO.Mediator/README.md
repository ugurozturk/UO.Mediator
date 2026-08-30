# UO.Mediator

Lightweight, framework-neutral request dispatcher / mediator for .NET applications.

## Requirements

- .NET 10

## Installation

```bash
dotnet add package UO.Mediator
dotnet add package UO.Mediator.Generators
```

## Quick start

Reference `UO.Mediator.Generators` as a private analyzer asset in each assembly that owns
handlers. For an assembly named `My.Application`, register its generated graph with:

```csharp
using UO.Mediator.Generated;

services.AddMyApplicationUOMediator();
```

The generated method explicitly registers the assembly's handlers, closed behaviors, and
strongly typed request routes. It does not scan the assembly or construct closed generic
executors at runtime.

Define a request and a handler:

```csharp
using UO.Mediator.Dispatching;

public sealed partial record CreateOrderRequest(string CustomerName) : IRequest<Guid>;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderRequest, Guid>
{
    public Task<Guid> HandleAsync(CreateOrderRequest request)
    {
        return Task.FromResult(Guid.NewGuid());
    }
}
```

Dispatch from an application service:

```csharp
public class OrderService(IRequestDispatcher dispatcher)
{
    public Task<Guid> CreateAsync(CreateOrderRequest input)
    {
        return dispatcher.DispatchAsync(input);
    }
}
```

## Commands without a response

```csharp
public sealed partial record SendNotificationRequest(string Message) : IRequest;

public sealed class SendNotificationHandler : IRequestHandler<SendNotificationRequest>
{
    public Task HandleAsync(SendNotificationRequest request)
    {
        // side-effect only
        return Task.CompletedTask;
    }
}
```

## Cancellation

Cancellation is owned by the consuming application. The mediator does not define or register
a cancellation token provider. A handler or behaviour that needs cancellation support injects
the provider chosen by its application and checks or forwards its token:

```csharp
public interface IApplicationCancellationTokenProvider
{
    CancellationToken Token { get; }
}

public sealed class ImportOrdersHandler(
    IApplicationCancellationTokenProvider cancellationTokenProvider)
    : IRequestHandler<ImportOrdersRequest>
{
    public async Task HandleAsync(ImportOrdersRequest request)
    {
        cancellationTokenProvider.Token.ThrowIfCancellationRequested();

        await ImportAsync(
            request,
            cancellationTokenProvider.Token);
    }
}
```

The dispatcher does not call `ThrowIfCancellationRequested()` automatically. Handlers and
behaviours that do not inject an application-level provider remain cancellation-agnostic.
Applications can use an existing framework provider or define their own abstraction.

## Pipeline behaviours

Implement `IRequestBehavior<TRequest, TResponse>` to add cross-cutting concerns:

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>
    : IRequestBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public int Order => 0;

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerNext<TRequest, TResponse> next)
    {
        // before handler
        var response = await next.InvokeAsync();
        // after handler
        return response;
    }
}
```

`RequestHandlerNext<TRequest, TResponse>` is an immutable readonly struct. This is
a breaking behavior API change from `RequestHandlerDelegate<TResponse>` and removes
the captured delegate allocation previously created for every behavior stage.
`IRequestBehavior<TRequest, TResponse>` is now invariant in `TRequest` so the
continuation can keep the request strongly typed without boxing or a hot-path adapter.

Migrate existing behavior methods by replacing the delegate parameter and invocation:

```csharp
// Before
Task<TResponse> HandleAsync(
    TRequest request,
    RequestHandlerDelegate<TResponse> next) => next();

// After
Task<TResponse> HandleAsync(
    TRequest request,
    RequestHandlerNext<TRequest, TResponse> next) => next.InvokeAsync();
```

The built-in `RequestLoggingBehavior` runs at `Order = int.MinValue` and logs request
duration. Requests slower than the configured threshold are logged as warnings.

## Configuration

```csharp
services.AddMyApplicationUOMediator(options =>
{
    options.SlowRequestThreshold = TimeSpan.FromMilliseconds(500);
});
```

Call each generated method when the application contains handlers from multiple assemblies.
The reflection-compatible registration API remains available for existing applications:

```csharp
services.AddUOMediator(typeof(CreateOrderHandler).Assembly);
```

## Startup validation

Call `ValidateUOMediator()` during application initialization after the service provider
is built. It ensures:

- Every generated request registration resolves exactly one handler. The compatibility path
  also scans its registered assemblies for missing or duplicate handlers.
- Every handler can be resolved from dependency injection.

```csharp
serviceProvider.ValidateUOMediator();
```

## Migrating from 1.x

Version 2.0 removes all `CancellationToken` parameters from dispatcher, handler and behaviour
contracts.

1. Remove `CancellationToken` parameters from `DispatchAsync` calls and `HandleAsync`
   implementations.
2. Remove registrations and adapters for
   `UO.Mediator.Dispatching.IRequestCancellationTokenProvider`.
3. Inject the consuming application's own cancellation provider only into handlers or
   behaviours that actively check cancellation or pass a token to another API.

## Design notes

- Generated registration emits transient handler and closed-behavior registrations for each
  handler assembly. Registrations supplied by the application first are preserved, including
  scoped and singleton lifetimes.
- A top-level, non-generic `partial` request receives an O(1) strongly typed self-route. Its
  successful dispatch bypasses runtime request-type and executor dictionary lookup.
- Non-partial, nested, and generic request types use the generated executor registry fallback.
  They still avoid runtime `MakeGenericType` and `Activator.CreateInstance`.
- `AddUOMediator(Assembly[])` remains a compatibility path that scans assemblies and creates
  closed generic executors on first use.
- Multiple behaviours for the same request/response pair are supported. Generated
  registration uses a per-assembly index to preserve existing registrations without
  quadratic duplicate checks.
- The existing readonly-struct behavior pipeline remains runtime-managed so DI lifetimes,
  ordering, short-circuiting, and repeated/concurrent `next` calls keep the same semantics.
- Generated routes cache code and immutable pipeline metadata only. Handler, behavior,
  request, scope, and service-provider instances are never cached by generated code.
- The package does not replace unit of work, authorization or feature management boundaries.
  Keep those concerns in the consuming framework or inside handlers as appropriate.

See the measured architecture, startup/dispatch benchmarks, NativeAOT proof, and adoption
decision in
[`docs/source-generated-dispatch-evaluation.md`](../../docs/source-generated-dispatch-evaluation.md).

## ASP.NET Core API generation

Install the companion `UO.Mediator.ApiExplorer` analyzer package in an
ASP.NET Core API host to generate controllers at compile time for requests
marked with `MediatorApiExplorerAttribute`. Requests can be grouped into a
stable, extensible partial controller with `ControllerName`; no runtime
reflection is used.

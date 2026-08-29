# UO.Mediator.Shared

Framework-neutral contracts shared by projects that use `UO.Mediator`.

`UO.Mediator.Shared` contains the request, handler, behaviour and dispatcher
abstractions under the `UO.Mediator.Dispatching` namespace. It has no dependency
on dependency injection, logging, ASP.NET Core or another application framework.

Use this package in contracts or abstractions projects that need to declare
mediator requests without referencing the runtime implementation. Application
hosts should reference the main `UO.Mediator` package, which includes the
dispatcher implementation and depends on this package.

## Requirements

- .NET 10

## Installation

```bash
dotnet add package UO.Mediator.Shared
```

If the project already references `UO.Mediator`, a separate reference to
`UO.Mediator.Shared` is not required.

## Define shared request contracts

```csharp
using UO.Mediator.Dispatching;

public sealed record OrderDto(Guid Id, string CustomerName);

public sealed record GetOrderQuery(Guid Id) : IRequest<OrderDto?>;

public sealed record CreateOrderCommand(string CustomerName) : IRequest<Guid>;

public sealed record CancelOrderCommand(Guid Id) : IRequest;
```

These contracts can live in a shared assembly referenced by both the caller and
the application that handles them.

## Implement handlers in the application

The application project can implement the contracts using the same abstractions:

```csharp
using UO.Mediator.Dispatching;

public sealed class GetOrderHandler
    : IRequestHandler<GetOrderQuery, OrderDto?>
{
    public Task<OrderDto?> HandleAsync(GetOrderQuery request)
    {
        // Load the order from the application's data source.
        return Task.FromResult<OrderDto?>(null);
    }
}

public sealed class CancelOrderHandler
    : IRequestHandler<CancelOrderCommand>
{
    public Task HandleAsync(CancelOrderCommand request)
    {
        // Perform the command without returning a response.
        return Task.CompletedTask;
    }
}
```

The application host must reference `UO.Mediator` and register the assemblies
that contain the handlers:

```csharp
services.AddUOMediator(typeof(GetOrderHandler).Assembly);
```

## Included contracts

- `IRequest` and `IRequest<TResponse>`
- `IRequestHandler<TRequest>` and
  `IRequestHandler<TRequest, TResponse>`
- `IRequestBehavior<TRequest, TResponse>`
- `RequestHandlerNext<TRequest, TResponse>`
- `IRequestDispatcher`
- `RequestDispatcherOptions`
- `Unit`

`IRequestBehavior<TRequest, TResponse>` is invariant in `TRequest`. Behavior
implementations receive the immutable readonly struct continuation and invoke the
downstream pipeline with `next.InvokeAsync()`. This is an intentional breaking change
from the former contravariant delegate-based behavior contract, made to avoid a
captured delegate allocation at every behavior stage.

This package provides contracts only. It does not scan assemblies, register
handlers, validate the request graph or dispatch requests. Those runtime
features are provided by `UO.Mediator`.

## Cancellation

The contracts intentionally do not include `CancellationToken` parameters.
Applications that need cancellation can inject their own cancellation provider
into handlers or behaviours and forward its token to the APIs they call.

## Package selection

| Package | Use it for |
| --- | --- |
| `UO.Mediator.Shared` | Declaring requests and mediator abstractions without runtime dependencies |
| `UO.Mediator` | Registering handlers, validating the request graph and dispatching requests |
| `UO.Mediator.ApiExplorer` | Generating ASP.NET Core controllers for exposed requests at compile time |

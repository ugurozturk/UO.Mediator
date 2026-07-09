# Uozturk.Mediator

Lightweight request dispatcher / mediator for .NET applications. It keeps request handlers and pipeline behaviours in a small framework-neutral package.

## Installation

```bash
dotnet add package Uozturk.Mediator
```

## Quick start

Register the dispatcher and explicitly list the assemblies that contain handlers and behaviours:

```csharp
services.AddUozturkMediator(typeof(CreateOrderHandler).Assembly);
```

Define a request and a handler:

```csharp
using Uozturk.Mediator.Dispatching;

public sealed record CreateOrderRequest(string CustomerName) : IRequest<Guid>;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderRequest, Guid>
{
    public Task<Guid> HandleAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Guid.NewGuid());
    }
}
```

Dispatch from an application service:

```csharp
public class OrderAppService : ApplicationService
{
    private readonly IRequestDispatcher _dispatcher;

    public OrderAppService(IRequestDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task<Guid> CreateAsync(CreateOrderRequest input)
    {
        return await _dispatcher.DispatchAsync(input);
    }
}
```

## Commands without a response

```csharp
public sealed record SendNotificationRequest(string Message) : IRequest;

public sealed class SendNotificationHandler : IRequestHandler<SendNotificationRequest>
{
    public Task HandleAsync(SendNotificationRequest request, CancellationToken cancellationToken = default)
    {
        // side-effect only
        return Task.CompletedTask;
    }
}
```

## Pipeline behaviours

Implement `IRequestBehavior<TRequest, TResponse>` to add cross-cutting concerns:

```csharp
public sealed class ValidationBehavior<TRequest, TResponse> : IRequestBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public int Order => 0;

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // before handler
        var response = await next();
        // after handler
        return response;
    }
}
```

The built-in `RequestLoggingBehaviour` runs at `Order = int.MinValue` and logs request duration. Requests slower than the configured threshold are logged as warnings.

## Configuration

```csharp
services.AddUozturkMediator(options =>
{
    options.SlowRequestThreshold = TimeSpan.FromMilliseconds(500);
}, typeof(CreateOrderHandler).Assembly);
```

## Startup validation

Call `ValidateUozturkMediator()` during startup after the service provider is built. It ensures every concrete request in the registered assemblies has exactly one handler and that the handler can be resolved from DI.

```csharp
serviceProvider.ValidateUozturkMediator();
```

## Design notes

- Handlers are registered as transient services from the assemblies passed to `AddUozturkMediator`.
- Multiple behaviours for the same request/response pair are supported via `TryAddEnumerable`.
- The dispatcher caches a closed-generic executor per `(request, response)` pair, so reflection is not used per dispatch.
- This package does **not** replace unit of work, authorization or feature management boundaries. Keep those concerns in the application framework or inside handlers as appropriate.

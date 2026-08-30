# UO.Mediator

UO.Mediator is a small, framework-neutral request dispatcher / mediator library for .NET applications. It routes each request to the appropriate handler, executes ordered pipeline behaviors, and preserves Microsoft dependency injection lifetimes.

The library is not intended to replace ABP application services, authorization, feature management, validation, or Unit of Work infrastructure. In large ABP monoliths, UO.Mediator is best used to split long application-layer workflows into small, focused handlers.

## Table of contents

- [Packages](#packages)
- [Quick start](#quick-start)
- [Recommended layout for UO.Mediator in a layered ABP application](#recommended-layout-for-uomediator-in-a-layered-abp-application)
- [ABP integration](#abp-integration)
- [Provider-specific EF Core queries in ABP](#provider-specific-ef-core-queries-in-abp)
- [Pipeline behaviors](#pipeline-behaviors)
- [Cancellation](#cancellation)
- [Startup validation](#startup-validation)
- [Recommendations for large monoliths](#recommendations-for-large-monoliths)
- [ASP.NET Core controller generation](#aspnet-core-controller-generation)
- [Design and lifetime notes](#design-and-lifetime-notes)
- [Repository structure](#repository-structure)

## Requirements

- .NET 10
- A dependency injection container compatible with Microsoft.Extensions.DependencyInjection

## Packages

| Package | Where should it be installed? | Purpose |
| --- | --- | --- |
| `UO.Mediator.Shared` | Contracts or Application projects | Request, handler, behavior, and dispatcher contracts |
| `UO.Mediator` | The application composition root; usually `HttpApi.Host` in ABP | Runtime registration, assembly scanning, dispatch, pipelines, and graph validation |
| `UO.Mediator.ApiExplorer` | The ASP.NET Core API host only | Compile-time controller generation for annotated requests |

A project that already references `UO.Mediator` does not need a separate reference to `UO.Mediator.Shared`.

```bash
dotnet add package UO.Mediator
```

## Quick start

Explicitly register the assemblies that contain your handlers:

```csharp
using UO.Mediator;

services.AddUOMediator(typeof(CreateOrderHandler).Assembly);
```

Define a request that returns a response and its handler:

```csharp
using UO.Mediator.Dispatching;

public sealed record CreateOrderCommand(string CustomerName) : IRequest<Guid>;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateOrderCommand request)
    {
        return Task.FromResult(Guid.NewGuid());
    }
}
```

Dispatch it from a caller:

```csharp
public sealed class OrderService(IRequestDispatcher dispatcher)
{
    public Task<Guid> CreateAsync(string customerName)
    {
        return dispatcher.DispatchAsync(new CreateOrderCommand(customerName));
    }
}
```

Commands without a response use `IRequest` and `IRequestHandler<TRequest>`:

```csharp
public sealed record RebuildOrderIndexCommand : IRequest;

public sealed class RebuildOrderIndexHandler : IRequestHandler<RebuildOrderIndexCommand>
{
    public Task HandleAsync(RebuildOrderIndexCommand request)
    {
        return Task.CompletedTask;
    }
}
```

## Recommended layout for UO.Mediator in a layered ABP application

The following layout is the recommended way to use UO.Mediator in a standard layered ABP solution.

These are **UO.Mediator integration recommendations**, not requirements imposed by the ABP Framework.

| ABP project | Recommended responsibility |
| --- | --- |
| `*.Domain.Shared` | Shared domain enums, constants, localization resources, and other domain-level shared contracts. UO.Mediator requests and handlers do not belong here. |
| `*.Domain` | Entities, aggregates, value objects, domain services/managers, repository abstractions, and domain business rules. Application request handlers do not belong here. |
| `*.Application.Contracts` | Public AppService interfaces and DTOs. Mediator request contracts may be placed here only when another application module intentionally needs to dispatch them directly. |
| `*.Application` | The primary home for UO.Mediator requests, commands, queries, handlers, and application-specific behaviors. Application orchestration belongs here. |
| `*.EntityFrameworkCore` | `DbContext`, EF Core mappings, repository implementations, and provider-specific persistence/query implementations. Application mediator handlers do not belong here. |
| `*.HttpApi` | Manually implemented HTTP controllers and HTTP-specific contracts when needed. |
| `*.HttpApi.Host` | The executable composition root. It references the `UO.Mediator` runtime, calls `AddUOMediator` with the handler assemblies, and optionally references `UO.Mediator.ApiExplorer`. |

### Typical dependency placement

The Application layer owns the mediator workflows and references `UO.Mediator.Shared`. The executable host references the `UO.Mediator` runtime and registers the handler assemblies:

```text
Application.Contracts -> UO.Mediator.Shared  (only when requests are intentionally shared)
Application           -> UO.Mediator.Shared
HttpApi.Host           -> UO.Mediator
HttpApi.Host           -> UO.Mediator.ApiExplorer  (optional)
```

Application workflows are normally organized in the Application project:

```text
*.Application
    ├── Requests
    ├── Commands
    ├── Queries
    ├── Handlers
    └── Behaviors
```

The HTTP API host may opt into compile-time API generation:

```xml
<PackageReference Include="UO.Mediator.ApiExplorer" Version="2.4.2" />
```

`UO.Mediator.ApiExplorer` is a compile-time source-generator/analyzer package. It does not introduce a runtime dependency merely to execute the generator.

The resulting responsibility flow is typically:

```text
HTTP API Host
      │
      │ generated API surface / ApiExplorer metadata (optional)
      ▼
Application Request
      │
      ▼
Application Handler
      │
      ├── Domain services
      ├── Repository abstractions
      └── Persistence/query abstractions
                 │
                 ▼
          EntityFrameworkCore
```

UO.Mediator is intended to model and dispatch **application use cases**. EF Core-specific persistence implementations remain infrastructure concerns and should not become mediator handlers merely to avoid direct dependencies.

Not every DTO needs to become a mediator request. The HTTP/API contract can remain an AppService DTO while the AppService maps that DTO to an internal command or query.

## ABP integration

### 1. Package references

The recommended minimum package distribution is:

```text
Application.Contracts -> UO.Mediator.Shared  (only when requests are shared)
Application           -> UO.Mediator.Shared
HttpApi.Host           -> UO.Mediator
```

### 2. Composition root registration

Register the runtime in the API host module and pass only the application assemblies that contain handlers:

```csharp
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator;

public override void ConfigureServices(ServiceConfigurationContext context)
{
    context.Services.AddUOMediator(
        typeof(BookStoreApplicationModule).Assembly);

    // Optional: request logging is not enabled by AddUOMediator.
    context.Services.AddUOMediatorRequestLogging(options =>
    {
        options.SlowRequestThreshold = TimeSpan.FromSeconds(2);
    });
}
```

A modular monolith can register multiple application assemblies:

```csharp
context.Services.AddUOMediator(
    typeof(SalesApplicationModule).Assembly,
    typeof(CatalogApplicationModule).Assembly,
    typeof(IdentityExtensionsApplicationModule).Assembly);
```

Do not add framework, Domain, EntityFrameworkCore, or host assemblies to the scan list unless they actually contain handlers.

A reusable, independent ABP module may register its own assembly from its ApplicationModule. For a large monolith owned by a single application, keeping all registrations in the host composition root makes it easier to see which modules are scanned.

### 3. Keep the AppService as the facade

Keep the ABP AppService as the HTTP and framework boundary:

```csharp
using Microsoft.AspNetCore.Authorization;
using UO.Mediator.Dispatching;
using Volo.Abp.Application.Services;

public class BookAppService(
    IRequestDispatcher dispatcher) : ApplicationService, IBookAppService
{
    [Authorize(BookStorePermissions.Books.Create)]
    public virtual Task<Guid> CreateAsync(CreateBookDto input)
    {
        return dispatcher.DispatchAsync(
            new CreateBookCommand(input.Name));
    }
}
```

The handler coordinates the application use case without reimplementing domain rules:

```csharp
using UO.Mediator.Dispatching;
using Volo.Abp.Domain.Repositories;

public sealed record CreateBookCommand(string Name) : IRequest<Guid>;

public sealed class CreateBookCommandHandler(
    BookManager bookManager,
    IRepository<Book, Guid> bookRepository)
    : IRequestHandler<CreateBookCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateBookCommand request)
    {
        var book = await bookManager.CreateAsync(request.Name);
        await bookRepository.InsertAsync(book);
        return book.Id;
    }
}
```

With this separation:

- `[Authorize]`, feature checks, and HTTP-specific validation remain at the AppService boundary.
- AppService methods remain `virtual`, and AppService classes remain compatible with ABP interception and proxy generation.
- The handler is resolved from the same request scope, so it can use the ambient tenant, current user, repository, and Unit of Work context.
- A rule that must hold regardless of the entry point belongs in a Domain entity or manager.
- Do not assume that handlers automatically participate in ABP attribute interception. Moving authorization or Unit of Work boundaries into handlers requires deliberate configuration and testing.

### 4. Background workers and event consumers

Do not capture the dispatcher from the root service provider. Create a scope inside a background job, worker, or event consumer—or use the scope already provided by ABP—and resolve the dispatcher from that scope. This keeps repositories, `DbContext`, tenant context, and Unit of Work services within their correct lifetimes.

## Provider-specific EF Core queries in ABP

Some queries cannot be implemented in the Application project because they depend on a specific EF Core provider, database function, SQL dialect, stored procedure, or PL/SQL API. Keep that provider-specific code in the `EntityFrameworkCore` project, but keep the application use case and its mediator handler in the `Application` project.

The recommended dependency flow is:

```text
AppService
   ↓
Application handler
   ↓
Query or repository abstraction
   ↓
Provider-specific implementation in EntityFrameworkCore
```

For a read-model or reporting query, the projects can be organized as follows when both `Application` and `EntityFrameworkCore` reference `Domain`:

```text
Acme.BookStore.Domain/
└── Books/Revenue/
    ├── BookRevenueData.cs
    └── IBookRevenueQuery.cs

Acme.BookStore.Application.Contracts/
└── Books/Revenue/
    └── BookRevenueDto.cs

Acme.BookStore.Application/
└── Books/Revenue/
    ├── GetBookRevenueQuery.cs
    └── GetBookRevenueQueryHandler.cs

Acme.BookStore.EntityFrameworkCore/
└── Books/Revenue/
    └── EfCoreBookRevenueQuery.cs
```

The Domain project contains the provider-neutral port and result model. It does not reference EF Core or expose provider types:

```csharp
public sealed record BookRevenueData(
    Guid BookId,
    string BookName,
    decimal Revenue);

public interface IBookRevenueQuery
{
    Task<IReadOnlyList<BookRevenueData>> ExecuteAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken);
}
```

The public Application DTO remains in `Application.Contracts`:

```csharp
public sealed record BookRevenueDto(
    Guid BookId,
    string BookName,
    decimal Revenue);
```

The Application handler remains provider-neutral and maps the Domain result to the Application DTO:

```csharp
using UO.Mediator.Dispatching;
using Volo.Abp.Threading;

public sealed record GetBookRevenueQuery(
    DateTime From,
    DateTime To) : IRequest<IReadOnlyList<BookRevenueDto>>;

public sealed class GetBookRevenueQueryHandler(
    IBookRevenueQuery revenueQuery,
    ICancellationTokenProvider cancellationTokenProvider)
    : IRequestHandler<GetBookRevenueQuery, IReadOnlyList<BookRevenueDto>>
{
    public async Task<IReadOnlyList<BookRevenueDto>> HandleAsync(
        GetBookRevenueQuery request)
    {
        var rows = await revenueQuery.ExecuteAsync(
            request.From,
            request.To,
            cancellationTokenProvider.Token);

        return rows
            .Select(row => new BookRevenueDto(
                row.BookId,
                row.BookName,
                row.Revenue))
            .ToArray();
    }
}
```

The `EntityFrameworkCore` project implements the abstraction and contains all provider-specific APIs and SQL:

```csharp
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

public sealed class EfCoreBookRevenueQuery(
    IDbContextProvider<BookStoreDbContext> dbContextProvider)
    : IBookRevenueQuery, ITransientDependency
{
    public async Task<IReadOnlyList<BookRevenueData>> ExecuteAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync();

        // Keep provider-specific EF functions, SQL dialect, stored procedure,
        // Npgsql extensions, or Oracle PL/SQL integration in this project.
        return await ExecuteProviderSpecificQueryAsync(
            dbContext,
            from,
            to,
            cancellationToken);
    }
}
```

`ITransientDependency` allows ABP conventional dependency injection to register the implementation. Explicit DI registration can be used instead when that better fits the module.

Choose the abstraction according to the responsibility:

| Scenario | Recommended abstraction | Interface location | Implementation location |
| --- | --- | --- | --- |
| Reporting, projection, search, or database-specific read model | Dedicated query port such as `IBookRevenueQuery` | `Domain`, or a dedicated abstractions project referenced by both layers | `EntityFrameworkCore` |
| Aggregate persistence with domain meaning | Custom repository such as `IBookRepository` | `Domain` | `EntityFrameworkCore` |
| Database migration, repair, or infrastructure-only maintenance | Infrastructure service; optionally an infrastructure handler | `EntityFrameworkCore` | `EntityFrameworkCore` |

An infrastructure-level handler is acceptable when the operation is genuinely infrastructure-only and is not part of the normal application API. For an application use case, placing the handler in `EntityFrameworkCore` is discouraged because it couples the request and orchestration directly to one persistence provider.

Some solutions deliberately add an `EntityFrameworkCore` reference to `Application.Contracts`, but that reference should not be introduced merely to share a query abstraction. When the existing dependency graph is `Application → Domain` and `EntityFrameworkCore → Domain`, placing the provider-neutral port in `Domain` follows dependency inversion without creating an additional cross-layer reference. If reporting abstractions become numerous and do not fit the Domain project, introduce a small shared abstractions project that both layers can reference.

When the handler stays in `Application`:

- The application use case remains independent of PostgreSQL, Oracle, or another provider.
- Provider-specific dependencies do not leak into request contracts or AppServices.
- The EF Core implementation participates in the current ABP scope and ambient Unit of Work.
- Tests can replace the query abstraction without constructing the real provider.
- Only the Application assembly needs to be passed to `AddUOMediator`; the EF Core assembly is registered through ABP dependency injection rather than mediator handler scanning.

## Pipeline behaviors

Behaviors can implement validation, measurement, tracing, or other application-specific cross-cutting work:

```csharp
public sealed class CreateBookValidationBehavior
    : IRequestBehavior<CreateBookCommand, Guid>
{
    public int Order => 0;

    public async Task<Guid> HandleAsync(
        CreateBookCommand request,
        RequestHandlerNext<CreateBookCommand, Guid> next)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Book name is required.");
        }

        return await next.InvokeAsync();
    }
}
```

Closed generic behaviors are discovered while scanning handler assemblies. An open generic behavior that should apply to every request must be registered explicitly with dependency injection:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

context.Services.TryAddEnumerable(
    ServiceDescriptor.Transient(
        typeof(IRequestBehavior<,>),
        typeof(ApplicationTracingBehavior<,>)));
```

Lower `Order` values run earlier in the pipeline. A stable ordering is applied when multiple behaviors have the same order.

Request logging is not enabled by `AddUOMediator`. Opt in explicitly when the application needs the built-in `RequestLoggingBehavior`:

```csharp
context.Services.AddUOMediatorRequestLogging(options =>
{
    options.SlowRequestThreshold = TimeSpan.FromSeconds(2);
});
```

The logging behavior runs at `Order = int.MinValue`, logs the request name and duration, and writes a warning when the configured threshold is exceeded. `SlowRequestThreshold` defaults to one second. Applications that use their own logging, tracing, or OpenTelemetry behavior do not need to register it.

Do not use behaviors as a general replacement for ABP authorization, auditing, or Unit of Work interceptors. A concern that belongs at the framework boundary should remain at that boundary.

## Cancellation

UO.Mediator contracts intentionally do not expose `CancellationToken` parameters. Cancellation is opt-in through the consuming application's own provider mechanism.

In an ABP application, a handler that needs cancellation can inject ABP's ambient `ICancellationTokenProvider` and pass its token to the actual asynchronous I/O boundary:

```csharp
using Volo.Abp.Threading;

public sealed class ImportBooksHandler(
    ICancellationTokenProvider cancellationTokenProvider,
    IBookImportService importService)
    : IRequestHandler<ImportBooksCommand>
{
    public async Task HandleAsync(ImportBooksCommand request)
    {
        cancellationTokenProvider.Token.ThrowIfCancellationRequested();

        await importService.ImportAsync(
            request.Source,
            cancellationTokenProvider.Token);
    }
}
```

Handlers that do not need cancellation do not need to inject the provider. The dispatcher does not perform an implicit cancellation check.

## Startup validation

`ValidateUOMediator()` detects the following problems during application startup:

- A request without a handler
- More than one handler for the same request
- A handler that cannot be constructed by the DI container

Standard .NET host:

```csharp
var app = builder.Build();
app.Services.ValidateUOMediator();
```

ABP host:

```csharp
public override void OnApplicationInitialization(
    ApplicationInitializationContext context)
{
    var environment = context.ServiceProvider
        .GetRequiredService<IHostEnvironment>();

    if (environment.IsDevelopment())
    {
        context.ServiceProvider.ValidateUOMediator();
    }
}
```

The validator scans the registered assemblies and resolves every handler graph inside a scope. In a large ABP monolith with thousands of handlers, this can increase production startup time and temporary memory usage. Running validation in development, tests, or CI is therefore recommended. Enabling it in production should be a deliberate decision based on measurements from the real application.

## Recommendations for large monoliths

The UO.Mediator runtime dispatch path is suitable for a typical ABP monolith with 4,000–5,000 handlers whose work is dominated by database, cache, file system, or external service I/O.

- Dispatch does not scan the complete handler list. It retrieves the executor for the request/response type from a cache.
- Closed generic executor creation and the required reflection work occur the first time a request type is dispatched, not on every call.
- The pipeline caches immutable wiring metadata only. It does not cache request, handler, behavior, or scoped dependency instances.
- Handlers and behaviors are resolved again from the current request or job scope, preserving transient and scoped lifetimes.
- Process-lifetime type caches are expected in a monolith with a fixed set of application assemblies. Dynamic plugin unloading scenarios require separate evaluation.

For a large application, pay particular attention to the following:

1. Pass only assemblies that contain handlers to `AddUOMediator`.
2. Measure startup validation in the real application; do not automatically enable it in production.
3. Do not register handlers as singletons. The default transient registration can safely consume scoped ABP services.
4. Do not split trivial CRUD operations merely to introduce a mediator request. Create a handler when there is a meaningful use-case or orchestration boundary.
5. Optimize I/O duration, slow-request logs, and database query counts. In an I/O-heavy system, these costs matter far more than the dispatcher's nanosecond-scale overhead.

## ASP.NET Core controller generation

`UO.Mediator.ApiExplorer` generates compile-time ASP.NET Core controllers in the API host assembly for annotated public requests:

```bash
dotnet add package UO.Mediator.ApiExplorer
```

```csharp
using UO.Mediator.ApiExplorer;
using UO.Mediator.Dispatching;

[MediatorApiExplorer(
    ControllerName = "Book",
    AuthorizationPolicy = "Books.Create")]
public sealed record CreateBookRequest(string Name) : IRequest<Guid>;
```

The package generates standard ASP.NET Core MVC controllers and OpenAPI metadata. The recommended default in an ABP application is to keep AppServices and ABP conventional controllers. Use ApiExplorer only when you intentionally want to expose a request directly as an HTTP endpoint; a generated controller bypasses the AppService facade.

Standard `IApiDescription` / OpenAPI visibility does not guarantee that an endpoint participates in ABP remote service metadata or the `abp generate-proxy` contract. If ABP client proxy generation is required, verify that integration separately.

See the [ApiExplorer package documentation](src/UO.Mediator.ApiExplorer/README.md) for route, HTTP method, authorization, and controller base options.

## Design and lifetime notes

- Every concrete request must have exactly one handler.
- Handlers and behaviors discovered through assembly scanning are registered as transient services by default.
- A request can have multiple behaviors.
- Request logging is optional and must be enabled with `AddUOMediatorRequestLogging`.
- Handlers and behaviors are resolved from the current `IServiceProvider` scope during dispatch.
- The behavior pipeline caches ordering metadata, not scoped service instances.
- `RequestHandlerNext<TRequest, TResponse>` is an immutable `readonly struct` and is invoked with `next.InvokeAsync()`.
- The dispatcher does not replace Unit of Work, authorization, feature management, or domain event infrastructure.
- Requests should primarily carry data; business logic belongs in handlers, application services, or domain services.

## Repository structure

```text
src/
├── UO.Mediator/              Runtime dispatcher, DI registration, and validation
├── UO.Mediator.Shared/       Framework-neutral public contracts
└── UO.Mediator.ApiExplorer/  Roslyn controller source generator

test/
├── UO.Mediator.Tests/
└── UO.Mediator.ApiExplorer.Tests/

demo/
├── UO.Mediator.Demo/         ASP.NET Core API example
└── UO.Mediator.Benchmarks/   BenchmarkDotNet scenarios
```

Package-specific documentation:

- [UO.Mediator runtime documentation](src/UO.Mediator/README.md)
- [UO.Mediator.Shared documentation](src/UO.Mediator.Shared/README.md)
- [UO.Mediator.ApiExplorer documentation](src/UO.Mediator.ApiExplorer/README.md)
- [Benchmark documentation](demo/UO.Mediator.Benchmarks/README.md)

## Build and test

```bash
dotnet restore UO.Mediator.slnx
dotnet build UO.Mediator.slnx
dotnet test UO.Mediator.slnx
```

Run the demo API:

```bash
dotnet run --project demo/UO.Mediator.Demo
```

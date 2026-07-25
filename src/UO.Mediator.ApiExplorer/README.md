# UO.Mediator.ApiExplorer

Compile-time ASP.NET Core controller generation for requests dispatched by
`UO.Mediator`.

The package is installed in the ASP.NET Core API host as a Roslyn analyzer. It
finds referenced public request classes and records marked with
`MediatorApiExplorerAttribute`, then generates ordinary `ControllerBase`
controllers in the host assembly. It performs no runtime reflection.

## Requirements

- .NET 10 API host
- ASP.NET Core MVC
- UO.Mediator 2.1 or later

## Installation

```bash
dotnet add package UO.Mediator
dotnet add package UO.Mediator.ApiExplorer
```

The API host must register MVC and the mediator as usual:

```csharp
builder.Services.AddControllers();
builder.Services.AddUOMediator(typeof(CreateBookHandler).Assembly);

var app = builder.Build();
app.MapControllers();
```

## Expose a request

The request can remain in an application or contracts assembly. The analyzer
runs in the API host and generates the controller there.

```csharp
using UO.Mediator.ApiExplorer;
using UO.Mediator.Dispatching;

[MediatorApiExplorer(ControllerName = "Book")]
public sealed record CreateBookRequest(string Name) : IRequest<Guid>;

[MediatorApiExplorer(ControllerName = "Book")]
public sealed record GetBookRequest(Guid Id) : IRequest<Book?>;
```

`CreateBookRequest` generates `POST /api/app/book`. The JSON body is bound to
the request and the generated controller forwards it to `IRequestDispatcher`.
Requests with the same `ControllerName` are emitted as separate actions in one
controller:

```csharp
public partial class BookController : ControllerBase
{
    public Task<ActionResult<Guid>> CreateBookAsync(CreateBookRequest request);
    public Task<ActionResult<Book?>> GetBookAsync(GetBookRequest request);
}
```

The generated controller is `partial`, so the API host can extend it from
another source file using the generated namespace
`UO.Mediator.ApiExplorer.Generated`. Partial declarations must be in the
API host compilation; they cannot be merged across assemblies.

## Naming conventions

| Request prefix | HTTP method |
| --- | --- |
| `GetList`, `GetAll`, `Get` | GET |
| `Put`, `Update` | PUT |
| `Delete`, `Remove` | DELETE |
| `Create`, `Add`, `Insert`, `Post` | POST |
| `Patch` | PATCH |
| Any other prefix | POST |

The recognized verb prefix and a trailing `Request`, `Command` or `Query` are
removed before the remaining name is converted to kebab-case. The generator
does not singularize or pluralize resource names. When `ControllerName` is not
set, the same remaining name is used as the controller group. Set
`ControllerName` explicitly when names such as `Book` and `Books` should share
one controller.

GET and DELETE requests are bound from the query string. POST, PUT and PATCH
requests are bound from the JSON body. `IRequest<TResponse>` returns the
response as JSON; `IRequest` returns HTTP 204 after a successful dispatch.

## Overrides

```csharp
[MediatorApiExplorer(
    ControllerName = "Catalog",
    Route = "/api/catalog/rebuild",
    HttpMethod = MediatorHttpMethod.Post,
    AuthorizationPolicy = "Catalog.Rebuild")]
public sealed record RebuildCatalogRequest : IRequest;
```

Set `AllowAnonymous = true` for a public endpoint. It cannot be combined with
`AuthorizationPolicy`. If neither option is specified, the generated
controller relies on the API host's global or fallback authorization policy.

Routes are absolute and static. Route parameters such as `{id}` are not
supported in the first version.

## Host naming options

The default convention root is `/api/app`. The API host can also add a
compile-time prefix and suffix to every generated controller:

```xml
<PropertyGroup>
  <UOMediatorApiRootPath>/api/catalog</UOMediatorApiRootPath>
  <UOMediatorControllerPrefix>App</UOMediatorControllerPrefix>
  <UOMediatorControllerSuffix>Service</UOMediatorControllerSuffix>
</PropertyGroup>
```

With `ControllerName = "Book"`, these settings generate the stable class name
`AppBookServiceController`. Routes are unaffected by the controller class
prefix and suffix. Changing these properties requires recompilation because
controller types are generated at compile time.

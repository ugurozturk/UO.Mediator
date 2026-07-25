# Uozturk.Mediator.ApiExplorer

Compile-time ASP.NET Core controller generation for requests dispatched by
`Uozturk.Mediator`.

The package is installed in the ASP.NET Core API host as a Roslyn analyzer. It
finds referenced public request classes and records marked with
`MediatorApiExplorerAttribute`, then generates ordinary `ControllerBase`
controllers in the host assembly. It performs no runtime reflection.

## Requirements

- .NET 10 API host
- ASP.NET Core MVC
- Uozturk.Mediator 2.1 or later

## Installation

```bash
dotnet add package Uozturk.Mediator.ApiExplorer
```

The API host must register MVC and the mediator as usual:

```csharp
builder.Services.AddControllers();
builder.Services.AddUozturkMediator(typeof(CreateBookHandler).Assembly);

var app = builder.Build();
app.MapControllers();
```

## Expose a request

The request can remain in an application or contracts assembly. The analyzer
runs in the API host and generates the controller there.

```csharp
using Uozturk.Mediator.ApiExplorer;
using Uozturk.Mediator.Dispatching;

[MediatorApiExplorer]
public sealed record CreateBookRequest(string Name) : IRequest<Guid>;
```

`CreateBookRequest` generates `POST /api/app/book`. The JSON body is bound to
the request and the generated controller forwards it to `IRequestDispatcher`.

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
does not singularize or pluralize resource names.

GET and DELETE requests are bound from the query string. POST, PUT and PATCH
requests are bound from the JSON body. `IRequest<TResponse>` returns the
response as JSON; `IRequest` returns HTTP 204 after a successful dispatch.

## Overrides

```csharp
[MediatorApiExplorer(
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

## Root path

The default convention root is `/api/app`. Override it in the API host project:

```xml
<PropertyGroup>
  <UozturkMediatorApiRootPath>/api/catalog</UozturkMediatorApiRootPath>
</PropertyGroup>
```

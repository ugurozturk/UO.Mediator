# UO.Mediator.Generators

Compile-time service registration and strongly typed request routes for
`UO.Mediator`.

## Installation

Reference the package as an analyzer in each assembly that owns handlers:

```xml
<PackageReference Include="UO.Mediator.Generators" Version="2.4.1">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

For an assembly named `My.Application`, the generator emits
`AddMyApplicationUOMediator()`:

```csharp
using UO.Mediator.Generated;

services.AddMyApplicationUOMediator(options =>
{
    options.SlowRequestThreshold = TimeSpan.FromMilliseconds(500);
});
```

The generated method contains explicit transient registrations for concrete handlers and
closed behaviors. An application registration made before the generated method is preserved,
so handlers and behaviors can still be configured as scoped or singleton. A per-assembly
registration index avoids quadratic `IServiceCollection` duplicate checks at large handler
counts.

Declare a top-level, non-generic request as `partial` to enable the fastest route:

```csharp
public sealed partial record PingRequest(int Value) : IRequest<int>;
```

The generator augments that request with an explicit O(1) dispatch route. A successful known
route bypasses `request.GetType()`, the runtime executor dictionaries, `MakeGenericType`, and
`Activator.CreateInstance`. The route contains no request, handler, behavior, scope, or service
provider instance. The selected handler and behaviors are resolved from the current Microsoft
DI scope on every dispatch.

Requests that cannot be augmented (for example, non-partial, nested, or generic request types)
use the generated executor registry compatibility route. That route still avoids runtime
closed-generic construction, but performs a request-type dictionary lookup.

Each handler assembly owns its generated registration method. Request contracts may live in a
separate generator-enabled assembly; the generated route then composes with handlers from one
or more other assemblies. Call every handler assembly's generated method. The existing
`AddUOMediator(Assembly[])` API remains available as a reflection-based compatibility path.
Do not mix the generated and compatibility registration styles in the same service
collection; choose one composition model for an application.

The generator emits a NativeAOT-safe closed registration for the built-in logging behavior.
Manually registered services remain runtime DI concerns. The generator intentionally keeps
the existing readonly-struct runtime behavior pipeline so scoped/transient lifetimes,
ordering, short-circuiting, and repeated or concurrent `next` calls retain their current
semantics.

The measured architecture and adoption decision are recorded in
[`docs/source-generated-dispatch-evaluation.md`](../../docs/source-generated-dispatch-evaluation.md).

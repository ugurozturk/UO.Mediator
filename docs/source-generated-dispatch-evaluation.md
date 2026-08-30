# Source-generated dispatch evaluation

## Outcome

UO.Mediator now has a real generated routing path, not only generated service metadata.
Top-level, non-generic `partial` request types receive an explicit self-route that calls the
existing strongly typed execution pipeline. The successful path performs one interface test
and does not call `request.GetType()`, query an executor dictionary, close a generic type, or
activate an executor dynamically.

The generated route is adopted as an opt-in path through `UO.Mediator.Generators`. The
reflection dispatcher remains the compatibility path. The source generator is justified by
the warmed dispatch improvement, lower first-use allocation, compile-time diagnostics, and a
clean NativeAOT story. It is not justified as a general startup optimization: the measured
default generated registration is slower at 100 and 1000 handlers.

## Runtime shape and invariants

```text
IRequest<TResponse> reference
  -> generated-route interface test
  -> request-owned closed generic route
  -> RequestExecution<TRequest,TResponse>
  -> resolve handler and behaviors from the current IServiceProvider scope
  -> existing readonly-struct behavior continuation
```

Generated code contains only route code and immutable registration/type metadata. It never
caches request instances, handler instances, behaviors, scoped services, or an
`IServiceProvider`. Microsoft DI resolves handlers and behaviors on every dispatch. Tests
cover transient, scoped, and singleton handlers and behaviors, behavior execution, response
requests, direct no-response requests, and `IRequest<Unit>` dispatch. The zero-behavior
no-response route still calls the handler directly.

If a request cannot own a route because it is non-partial, nested, or generic, the generated
dispatcher lazily enters the generated executor-registry fallback. The reflection-compatible
dispatcher remains separate.

## Benchmark method

All results below were collected on an Apple M2 Ultra with .NET SDK 10.0.201 and .NET 10.0.5.
The A/B/C hot-path comparison used BenchmarkDotNet's default job. The 10/100/1000 fixture
comparisons used the short job (three measurements), so they are directional rather than
release-grade statistical claims. Raw reports are under `BenchmarkDotNet.Artifacts/results/`.

### A/B/C warmed dispatch

| Architecture | Mean | Allocation |
|---|---:|---:|
| martinothamar/Mediator (context only) | 31.19 ns | 88 B |
| MediatR (context only) | 48.60 ns | 200 B |
| C. UO generated strongly typed self-route | 52.75 ns | 128 B |
| B. UO generated executor registry | 56.68 ns | 128 B |
| A. UO runtime executor cache | 57.83 ns | 128 B |
| UO public default with logging | 114.04 ns | 264 B |

C is 5.08 ns (8.8%) faster than A and 3.93 ns (6.9%) faster than B in this run,
with unchanged allocation. Competitors are context, not a claim of contract-equivalent work;
martinothamar/Mediator uses `ValueTask` while UO.Mediator and MediatR use `Task`.

### Registration and provider construction

These results measure the default generated composition, including registration and subsequent
benchmark removal of one closed logging behavior descriptor per request. The runtime path adds
and removes one open-generic logging descriptor.

| Handlers | Runtime registration | Generated registration | Runtime provider build | Generated provider build | Runtime reg alloc | Generated reg alloc |
|---:|---:|---:|---:|---:|---:|---:|
| 10 | 1.785 us | 1.624 us | 1.851 us | 2.058 us | 4.69 KB | 8.75 KB |
| 100 | 18.701 us | 21.073 us | 4.046 us | 5.820 us | 29.79 KB | 52.89 KB |
| 1000 | 854.778 us | 1,134.438 us | 28.681 us | 45.538 us | 311.72 KB | 504.53 KB |

The first route-metadata implementation used repeated `TryAddEnumerable` scans and measured
5.4 ms at 1000 handlers. A per-assembly registration index reduced that result to 1.13 ms and
preserves application-supplied lifetimes, but it does not turn the final default generated
composition into a startup win.

### First and warmed dispatch at scale

First-use provider creation and dispatcher resolution are excluded. The runtime executor cache
is cleared before each measured runtime invocation. The first-use rows report medians because
each short-job iteration invokes once and the 1000-handler runtime mean contained a large
outlier.

| Handlers | Runtime first median | Generated first median | Runtime warmed | Generated warmed | Runtime first alloc | Generated first alloc |
|---:|---:|---:|---:|---:|---:|---:|
| 10 | 11.583 us | 11.500 us | 57.39 ns | 51.39 ns | 3,160 B | 2,696 B |
| 100 | 17.374 us | 18.958 us | 57.85 ns | 52.06 ns | 3,160 B | 2,696 B |
| 1000 | 63.292 us | 54.583 us | 58.46 ns | 52.28 ns | 3,160 B | 2,696 B |

Generated first use consistently allocates 464 B less. Latency is approximately equal at 10,
worse at 100, and better at 1000 in this short run; it is therefore a mixed result, not a
universal first-call speedup. Warmed generated dispatch stays O(1) from 10 through 1000 and is
about 6 ns faster than the runtime path at each size.

## NativeAOT, trimming, and composition proof

`demo/UO.Mediator.NativeAotSample` publishes with `PublishAot`, `PublishTrimmed`, and full trim
mode. A real `osx-arm64` native publish completed with zero trim/AOT warnings, and the native
binary dispatched a response request, a no-response request, and a behavior pipeline.

`demo/UO.Mediator.MultiAssemblySample` verifies requests in a contracts assembly, response
handlers and a behavior in one handler assembly, and a no-response handler in another. The
sample builds with zero warnings and composes both generated registration methods without
duplicate registrations.

The ApiExplorer integration test runs both source generators against the same compilation,
loads the emitted assembly into ASP.NET Core TestServer, and calls real generated response and
no-response controller routes.

## Competitor hot paths

[`martinothamar/Mediator`](https://github.com/martinothamar/Mediator) generates monomorphized
concrete `Send` overloads and closed pipeline registrations. Calls made through its interface
cannot know the concrete request statically, so its generated interface path uses a type switch
and changes to a dictionary above a configured size. The maintainer explains that tradeoff in
[discussion 213](https://github.com/martinothamar/Mediator/discussions/213), while
[issue 7](https://github.com/martinothamar/Mediator/issues/7) records the linear-switch scaling
problem. UO.Mediator's request-owned self-route avoids a central O(N) switch while retaining its
existing `IRequest<T>` dispatcher API.

[`Immediate.Handlers`](https://immediateplatform.dev/docs/Immediate.Handlers/how-it-works)
generates a handler entry point and closed behavior chain per request. DI constructs that
generated chain, while the hot path directly calls the generated handler surface. This removes
more runtime pipeline assembly than UO.Mediator. UO.Mediator intentionally retains its existing
runtime pipeline and readonly-struct continuation to preserve ordering, short-circuiting,
repeated `next`, concurrency, and lifetime semantics with less generated surface area.

## Decision criteria

1. **Does generated registration materially improve startup?** No. It is 9% faster only at 10
   handlers; at 100 and 1000 handlers registration is 13% and 33% slower, and generated provider
   construction is also slower. Source generation should not be marketed as a startup win.
2. **Does generated routing improve steady-state dispatch?** Yes. The default-job A/B/C result
   is 52.75 ns versus 57.83 ns for the runtime cache, an 8.8% improvement with the same 128 B.
   The 10/100/1000 warmed fixtures show the same O(1) shape and about a 6 ns improvement.
3. **Does it reduce first-dispatch cost?** Partly. It removes dynamic executor creation and saves
   464 B, but short-job latency is mixed: equal at 10, slower at 100, faster at 1000.
4. **Does it enable a clean NativeAOT/trimming story?** Yes. Known routes and closed logging
   behavior registrations publish and execute as warning-free native code.
5. **What reflection/dynamic generic machinery remains?** None on a successful known self-route.
   Microsoft DI resolution and the existing immutable pipeline cache remain intentionally.
   Non-routable generated requests use `GetType()` plus the generated executor dictionary.
   The compatibility dispatcher still uses its runtime executor cache, `MakeGenericType`, and
   `Activator.CreateInstance` on first use.
6. **Is the added complexity justified?** Yes as an opt-in generated path for measurable warmed
   performance, allocation, diagnostics, and NativeAOT benefits; no as a replacement for the
   minimal reflection compatibility path or as a startup-only optimization. Keeping both paths
   makes the boundary explicit and avoids imposing source-generation constraints on all users.

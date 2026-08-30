# Mediator Benchmarks

This project compares request/response dispatch overhead for:

- UO.Mediator
- MediatR
- martinothamar/Mediator

Each benchmark reuses an immutable request and executes the same `Value + 1`
handler. Handlers use transient lifetime so the three dispatchers are compared
under the same DI lifetime. The measured operation excludes container creation,
mediator resolution, source generation, and first-call cache initialization.

> **Important:** The benchmarks use each library's native handler contract.
> martinothamar/Mediator uses `ValueTask`, while UO.Mediator and MediatR use
> `Task`. This difference can affect the benchmark results and should be taken
> into account when comparing the libraries.

UO.Mediator is shown four ways. `default logging` measures its reflection-compatible
public default, which always includes `RequestLoggingBehavior`. `core dispatch` removes that
descriptor inside the benchmark setup to isolate the compatibility dispatcher overhead.
`generated registry` uses the first source-generation prototype's runtime executor dictionary.
`generated routes` uses a compile-time emitted O(1) self-route on the partial request type and
bypasses runtime request type and executor dictionary lookup. Both generated cases remove the
same default logging behavior.

Additional UO.Mediator benchmark groups isolate specific dispatcher costs:

- `UOBehaviorPipelineBenchmarks` measures `0`, `1`, `3`, and `5` empty behaviours.
- `UODispatchShapeBenchmarks` compares response and no-response requests with both
  synchronously completed tasks and handlers that call `Task.Yield()`.
- `UOHandlerLookupBenchmarks` measures a warmed dispatch after preparing `10`, `100`,
  or `1000` distinct closed request/handler pipelines.
- `UOStartupRegistrationBenchmarks` compares reflection and source-generated service
  registration plus `ServiceProvider` construction for `10`, `100`, and `1000` handlers.
- `UOStartupDispatchBenchmarks` isolates first-use dispatch and warmed dispatch for the
  last request in each `10`, `100`, and `1000` handler fixture assembly.

For `first dispatch`, provider construction and dispatcher resolution happen in iteration
setup and are excluded from the measured operation. The runtime executor cache is cleared
before every measured runtime invocation, so the runtime result includes its real
`MakeGenericType`/`Activator.CreateInstance` first-use work. Each first-use iteration invokes
the dispatcher once; use the median alongside the mean because short jobs have only three
samples and BenchmarkDotNet correctly reports a minimum-iteration-time warning.

The registration benchmark measures the public default composition. Generated registration
therefore includes one closed NativeAOT-safe logging behavior registration per request;
the reflection path initially adds one open-generic logging registration. Both logging
descriptors are removed before provider construction and dispatch so warmed hot-path numbers
remain comparable.

All UO.Mediator benchmarks warm the executor and pipeline caches during global setup.
Each behavior and handler count is a separate benchmark method so BenchmarkDotNet
reports ratios against the `0 behaviors` and `10 handlers` baselines.

## Behavior Allocation Decomposition

The following diagnostic groups decompose the warmed behavior-pipeline cost without
changing the production dispatcher or pipeline implementation:

- `UOBehaviorLifetimeBenchmarks` dispatches the same request to the same singleton
  handler with `0`, `1`, `3`, or `5` empty behaviors. Transient and singleton
  behavior registrations use separate service providers. Their difference isolates
  transient behavior construction; both still include Microsoft DI collection
  resolution and UO.Mediator pipeline invocation.
- `UOBehaviorResolutionBenchmarks` calls Microsoft DI directly, without a dispatcher.
  It reports both `GetServices<T>()` and the current UO.Mediator `ResolveBehaviors`
  shape (`IReadOnlyList<T>` cast with a `ToArray()` fallback) for `0`, `1`, `3`, and
  `5` transient behaviors and `1`, `3`, and `5` singleton behaviors. This isolates
  DI resolution, lifetime-specific construction, and collection materialization.
- `UODirectBehaviorInvocationBenchmarks` uses fixed empty behavior instances and a
  manually wired benchmark-local delegate chain matching the former captured
  pipeline. The delegates are constructed once during setup, so this is a lower-bound
  baseline for nested behavior/delegate invocation without DI, dispatcher lookup, or
  per-call continuation construction.
- `UODirectHandlerBaselineBenchmarks` compares the existing synchronously completed
  `Task` handler directly with a warmed zero-behavior UO.Mediator dispatch using that
  same handler instance. The difference estimates base dispatcher and DI lookup
  overhead.
- `UOCachedPipelineGraphPrototypeBenchmarks` compares the current captured closure,
  readonly struct continuation, and a benchmark-only cached graph whose immutable
  steps retain only behavior positions and downstream delegates. Handler and
  behavior instances live in a new execution context for each dispatch.
- `UOCachedPipelineGraphPrototypeBenchmarksDi` measures those same continuation
  shapes after resolving a transient handler and `1`, `3`, or `5` singleton or
  transient behaviors from Microsoft DI on every invocation. Its setup also checks
  concurrent execution with distinct instances and reuse across scoped providers.

Transient and singleton behavior results measure different Microsoft DI lifetime
costs and should not be treated as interchangeable pipeline results. Raw DI
resolution is reported separately so it can be distinguished from continuation
costs. The prototype groups remain benchmark-only comparisons; the production
dispatcher now uses the readonly struct continuation architecture.

Run the full benchmark from the repository root:

```bash
dotnet run -c Release --project demo/UO.Mediator.Benchmarks -- --filter '*'
```

Use a short job while iterating:

```bash
dotnet run -c Release --project demo/UO.Mediator.Benchmarks -- --job short --filter '*'
```

To verify that every benchmark executes without collecting publishable numbers:

```bash
dotnet run -c Release --project demo/UO.Mediator.Benchmarks -- --job dry --inProcess --filter '*'
```

The in-process dry job is only a smoke test. Use the default isolated toolchain
for performance comparisons.

Run only the cached-graph experiment with a short development job:

```bash
dotnet run -c Release --project demo/UO.Mediator.Benchmarks -- \
  --filter '*UOCachedPipelineGraphPrototypeBenchmarks*' \
  -j short
```

Run the same experiment with BenchmarkDotNet's default job:

```bash
dotnet run -c Release --project demo/UO.Mediator.Benchmarks -- \
  --filter '*UOCachedPipelineGraphPrototypeBenchmarks*'
```

BenchmarkDotNet writes detailed Markdown, CSV, and HTML reports under
`BenchmarkDotNet.Artifacts/`. Run benchmarks on an idle machine in Release mode;
do not compare numbers collected on different machines or runtime versions.

The accepted results and decision are summarized in
[`docs/source-generated-dispatch-evaluation.md`](../../docs/source-generated-dispatch-evaluation.md).

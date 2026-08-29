# Mediator Benchmarks

This project compares request/response dispatch overhead for:

- UO.Mediator
- MediatR
- martinothamar/Mediator

Each benchmark reuses an immutable request and executes the same `Value + 1`
handler. Handlers use transient lifetime so the three dispatchers are compared
under the same DI lifetime. The measured operation excludes container creation,
mediator resolution, source generation, and first-call cache initialization.

UO.Mediator is shown twice. `default logging` measures its public default, which
always includes `RequestLoggingBehavior`. `core dispatch` removes that descriptor
inside the benchmark setup to isolate dispatcher overhead; this is not currently
a supported UO.Mediator configuration API.

Additional UO.Mediator benchmark groups isolate specific dispatcher costs:

- `UOBehaviorPipelineBenchmarks` measures `0`, `1`, `3`, and `5` empty behaviours.
- `UODispatchShapeBenchmarks` compares response and no-response requests with both
  synchronously completed tasks and handlers that call `Task.Yield()`.
- `UOHandlerLookupBenchmarks` measures a warmed dispatch after preparing `10`, `100`,
  or `1000` distinct closed request/handler pipelines.

All UO.Mediator benchmarks warm the executor and pipeline caches during global setup.
Each behavior and handler count is a separate benchmark method so BenchmarkDotNet
reports ratios against the `0 behaviors` and `10 handlers` baselines.

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

BenchmarkDotNet writes detailed Markdown, CSV, and HTML reports under
`BenchmarkDotNet.Artifacts/`. Run benchmarks on an idle machine in Release mode;
do not compare numbers collected on different machines or runtime versions.

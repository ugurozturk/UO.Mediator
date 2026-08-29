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

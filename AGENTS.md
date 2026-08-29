# Repository Guidelines

## Project Structure & Module Organization

The solution is organized by responsibility. `src/UO.Mediator/` contains the runtime dispatcher, dependency-injection registration, validation, and pipeline behavior support. `src/UO.Mediator.Shared/` holds request contracts shared with consumers. `src/UO.Mediator.ApiExplorer/` is the Roslyn source generator and includes its `buildTransitive/` MSBuild props and analyzer release notes. Tests mirror these areas under `test/`. `demo/UO.Mediator.Demo/` is an ASP.NET Core host that demonstrates generated controllers and OpenAPI output. Consult each package's local `README.md` when changing public behavior.

## Build, Test, and Development Commands

- `dotnet restore UO.Mediator.slnx` — restore all solution dependencies.
- `dotnet build UO.Mediator.slnx` — compile libraries, generator, tests, and demo.
- `dotnet test UO.Mediator.slnx` — run the complete xUnit suite.
- `dotnet run --project demo/UO.Mediator.Demo` — launch the demonstration API and Swagger UI.
- `dotnet pack src/UO.Mediator/UO.Mediator.csproj -c Release` — create a release package; repeat for `UO.Mediator.Shared` and `UO.Mediator.ApiExplorer` when validating packaging changes.

## Coding Style & Naming Conventions

Use four-space indentation, file-scoped namespaces, nullable reference types, and modern C# supported by each project. Runtime projects target .NET 10; the generator targets `netstandard2.0`, so keep generator APIs compatible with that target. Use PascalCase for public types and members, `_camelCase` for private fields, and the `Async` suffix for asynchronous methods. Request types normally end in `Request`, `Command`, or `Query`; handlers end in `Handler`. Do not edit generated files under `bin/` or `obj/`. Record new analyzer diagnostics in `AnalyzerReleases.Unshipped.md`.

## Testing Guidelines

Tests use xUnit. Name tests as behavioral statements such as `Should_Run_Behaviors_In_Order`. Add dispatcher tests to `UO.Mediator.Tests`; add generator compilation and ASP.NET Core `TestServer` coverage to `UO.Mediator.ApiExplorer.Tests`. There is no documented coverage threshold, but every behavior change should include regression coverage. For generator changes, assert emitted source, diagnostics, and successful compilation where applicable.

## Commit & Pull Request Guidelines

Recent history favors short, imperative subjects such as `Add support for configurable controller base class in API generator`; scoped prefixes such as `Refactor:` also appear. Keep each commit focused and explain compatibility or packaging effects in the body. Pull requests should summarize the change, identify affected packages, link relevant issues, list executed build/test commands, and update package documentation or the demo for public API and generated-route changes. Include generated-code or OpenAPI excerpts when they make generator behavior easier to review.

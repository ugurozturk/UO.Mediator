; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 2.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
UOMA001 | UO.Mediator.ApiExplorer | Error | Required host dependency is missing
UOMA002 | UO.Mediator.ApiExplorer | Error | Marked request type is unsupported
UOMA003 | UO.Mediator.ApiExplorer | Error | Generated endpoint route is invalid
UOMA004 | UO.Mediator.ApiExplorer | Error | Generated verb and route conflict
UOMA005 | UO.Mediator.ApiExplorer | Error | Authorization settings conflict
UOMA006 | UO.Mediator.ApiExplorer | Error | HTTP method setting is invalid
UOMA007 | UO.Mediator.ApiExplorer | Error | Generated controller configuration is invalid

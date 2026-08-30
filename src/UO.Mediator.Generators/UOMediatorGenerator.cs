using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UO.Mediator.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class UOMediatorGenerator : IIncrementalGenerator
{
    private const string RequestMetadataName = "UO.Mediator.Dispatching.IRequest";
    private const string GenericRequestMetadataName = "UO.Mediator.Dispatching.IRequest`1";
    private const string NoResponseHandlerMetadataName =
        "UO.Mediator.Dispatching.IRequestHandler`1";
    private const string ResponseHandlerMetadataName =
        "UO.Mediator.Dispatching.IRequestHandler`2";
    private const string BehaviorMetadataName =
        "UO.Mediator.Dispatching.IRequestBehavior`2";
    private const string UnitMetadataName = "UO.Mediator.Dispatching.Unit";
    private const string GeneratedNoResponseRouteMetadataName =
        "UO.Mediator.Dispatching.IGeneratedRequestRoute";
    private const string GeneratedResponseRouteMetadataName =
        "UO.Mediator.Dispatching.IGeneratedRequestRoute`1";

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
    private static readonly DiagnosticDescriptor DuplicateHandler = new(
        "UOMG001",
        "Duplicate request handler",
        "Request '{0}' has multiple handlers in assembly '{1}': {2}",
        "UO.Mediator.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingHandler = new(
        "UOMG002",
        "Request handler is missing",
        "Request '{0}' has no handler in assembly '{1}'",
        "UO.Mediator.Generators",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "The request may intentionally be handled by another assembly. " +
            "Validate the complete application graph at startup when that is the case.");

    private static readonly DiagnosticDescriptor InaccessibleService = new(
        "UOMG003",
        "Mediator service cannot be generated",
        "Type '{0}' implements a mediator service but is not accessible from generated assembly-level registration code",
        "UO.Mediator.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(
            context.CompilationProvider,
            static (productionContext, compilation) =>
                Generate(productionContext, compilation));
    }

    private static void Generate(
        SourceProductionContext context,
        Compilation compilation)
    {
        var requestType = compilation.GetTypeByMetadataName(RequestMetadataName);
        var genericRequestType = compilation.GetTypeByMetadataName(GenericRequestMetadataName);
        var noResponseHandlerType =
            compilation.GetTypeByMetadataName(NoResponseHandlerMetadataName);
        var responseHandlerType =
            compilation.GetTypeByMetadataName(ResponseHandlerMetadataName);
        var behaviorType = compilation.GetTypeByMetadataName(BehaviorMetadataName);
        var unitType = compilation.GetTypeByMetadataName(UnitMetadataName);
        var generatedNoResponseRouteType =
            compilation.GetTypeByMetadataName(GeneratedNoResponseRouteMetadataName);
        var generatedResponseRouteType =
            compilation.GetTypeByMetadataName(GeneratedResponseRouteMetadataName);

        if (requestType is null ||
            genericRequestType is null ||
            noResponseHandlerType is null ||
            responseHandlerType is null ||
            behaviorType is null ||
            unitType is null ||
            generatedNoResponseRouteType is null ||
            generatedResponseRouteType is null)
        {
            return;
        }

        var assemblyTypes = EnumerateTypes(compilation.Assembly.GlobalNamespace)
            .OrderBy(
                static type => type.ToDisplayString(TypeFormat),
                StringComparer.Ordinal)
            .ToArray();
        var handlers = new List<HandlerModel>();
        var behaviors = new List<BehaviorModel>();

        foreach (var type in assemblyTypes)
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType)
            {
                continue;
            }

            var noResponseContracts = type.AllInterfaces
                .Where(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    noResponseHandlerType))
                .ToArray();
            var noResponseRequests = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var contract in noResponseContracts)
            {
                noResponseRequests.Add(contract.TypeArguments[0]);
                handlers.Add(new HandlerModel(
                    contract.TypeArguments[0],
                    unitType,
                    type,
                    hasResponse: false,
                    hasGeneratedRoute: HasGeneratedRoute(
                        contract.TypeArguments[0],
                        generatedNoResponseRouteType,
                        generatedResponseRouteType,
                        hasResponse: false)));
            }

            foreach (var contract in type.AllInterfaces.Where(candidate =>
                         SymbolEqualityComparer.Default.Equals(
                             candidate.OriginalDefinition,
                             responseHandlerType)))
            {
                if (noResponseRequests.Contains(contract.TypeArguments[0]) &&
                    SymbolEqualityComparer.Default.Equals(contract.TypeArguments[1], unitType))
                {
                    continue;
                }

                handlers.Add(new HandlerModel(
                    contract.TypeArguments[0],
                    contract.TypeArguments[1],
                    type,
                    hasResponse: true,
                    hasGeneratedRoute: HasGeneratedRoute(
                        contract.TypeArguments[0],
                        generatedNoResponseRouteType,
                        generatedResponseRouteType,
                        hasResponse: true)));
            }

            foreach (var contract in type.AllInterfaces.Where(candidate =>
                         SymbolEqualityComparer.Default.Equals(
                             candidate.OriginalDefinition,
                             behaviorType)))
            {
                behaviors.Add(new BehaviorModel(
                    contract.TypeArguments[0],
                    contract.TypeArguments[1],
                    type));
            }

            if ((noResponseContracts.Length > 0 ||
                 type.AllInterfaces.Any(candidate =>
                     SymbolEqualityComparer.Default.Equals(
                         candidate.OriginalDefinition,
                         responseHandlerType) ||
                     SymbolEqualityComparer.Default.Equals(
                         candidate.OriginalDefinition,
                         behaviorType))) &&
                !IsAccessibleFromGeneratedCode(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InaccessibleService,
                    type.Locations.FirstOrDefault(),
                    type.ToDisplayString(TypeFormat)));
            }
        }

        handlers = handlers
            .Where(handler => IsAccessibleFromGeneratedCode(handler.ImplementationType))
            .Distinct(HandlerModelComparer.Instance)
            .OrderBy(handler => handler.RequestType.ToDisplayString(TypeFormat), StringComparer.Ordinal)
            .ThenBy(handler => handler.ResponseType.ToDisplayString(TypeFormat), StringComparer.Ordinal)
            .ThenBy(handler => handler.ImplementationType.ToDisplayString(TypeFormat), StringComparer.Ordinal)
            .ToList();
        behaviors = behaviors
            .Where(behavior => IsAccessibleFromGeneratedCode(behavior.ImplementationType))
            .Distinct(BehaviorModelComparer.Instance)
            .OrderBy(behavior => behavior.RequestType.ToDisplayString(TypeFormat), StringComparer.Ordinal)
            .ThenBy(behavior => behavior.ResponseType.ToDisplayString(TypeFormat), StringComparer.Ordinal)
            .ThenBy(behavior => behavior.ImplementationType.ToDisplayString(TypeFormat), StringComparer.Ordinal)
            .ToList();

        ReportHandlerDiagnostics(
            context,
            compilation,
            assemblyTypes,
            handlers,
            requestType,
            genericRequestType,
            unitType);

        var identifier = CreateIdentifier(compilation.AssemblyName ?? "Assembly");
        if (handlers.Count > 0 || behaviors.Count > 0)
        {
            context.AddSource(
                "UOMediatorRegistration.g.cs",
                RenderRegistration(identifier, handlers, behaviors));
        }

        var routableRequests = CreateRoutableRequests(
            assemblyTypes,
            requestType,
            genericRequestType,
            unitType);
        if (routableRequests.Count > 0)
        {
            context.AddSource(
                "UOMediatorRequestRoutes.g.cs",
                RenderRequestRoutes(routableRequests));
        }
    }

    private static void ReportHandlerDiagnostics(
        SourceProductionContext context,
        Compilation compilation,
        IReadOnlyList<INamedTypeSymbol> assemblyTypes,
        IReadOnlyList<HandlerModel> handlers,
        INamedTypeSymbol requestType,
        INamedTypeSymbol genericRequestType,
        INamedTypeSymbol unitType)
    {
        var groups = handlers.GroupBy(
            handler => new HandlerKey(
                handler.RequestType,
                handler.ResponseType,
                handler.HasResponse),
            HandlerKeyComparer.Instance);

        foreach (var group in groups.Where(group => group.Count() > 1))
        {
            var implementations = string.Join(
                ", ",
                group.Select(handler =>
                    handler.ImplementationType.ToDisplayString(TypeFormat)));
            foreach (var handler in group)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateHandler,
                    handler.ImplementationType.Locations.FirstOrDefault(),
                    handler.RequestType.ToDisplayString(TypeFormat),
                    compilation.AssemblyName,
                    implementations));
            }
        }

        foreach (var type in assemblyTypes.Where(type =>
                     type.TypeKind is TypeKind.Class or TypeKind.Struct &&
                     !type.IsAbstract &&
                     !type.IsGenericType))
        {
            var isNoResponse = type.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate, requestType));
            var responseContracts = type.AllInterfaces
                .Where(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    genericRequestType))
                .Where(candidate =>
                    !isNoResponse ||
                    !SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], unitType))
                .ToArray();

            if (isNoResponse && !handlers.Any(handler =>
                    !handler.HasResponse &&
                    SymbolEqualityComparer.Default.Equals(handler.RequestType, type)))
            {
                ReportMissingHandler(context, compilation, type);
            }

            foreach (var contract in responseContracts)
            {
                if (!handlers.Any(handler =>
                        handler.HasResponse &&
                        SymbolEqualityComparer.Default.Equals(handler.RequestType, type) &&
                        SymbolEqualityComparer.Default.Equals(
                            handler.ResponseType,
                            contract.TypeArguments[0])))
                {
                    ReportMissingHandler(context, compilation, type);
                }
            }
        }
    }

    private static void ReportMissingHandler(
        SourceProductionContext context,
        Compilation compilation,
        INamedTypeSymbol requestType)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            MissingHandler,
            requestType.Locations.FirstOrDefault(),
            requestType.ToDisplayString(TypeFormat),
            compilation.AssemblyName));
    }

    private static string RenderRegistration(
        string identifier,
        IReadOnlyList<HandlerModel> handlers,
        IReadOnlyList<BehaviorModel> behaviors)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace UO.Mediator.Generated;");
        source.AppendLine();
        source.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"UO.Mediator.Generators\", \"2.4.1\")]");
        source.AppendLine("[global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]");
        source.AppendLine($"public static class {identifier}UOMediatorRegistration");
        source.AppendLine("{");
        source.AppendLine($"    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{identifier}UOMediator(");
        source.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,");
        source.AppendLine("        global::System.Action<global::UO.Mediator.Dispatching.RequestDispatcherOptions>? configure = null)");
        source.AppendLine("    {");
        source.AppendLine("        global::UO.Mediator.UOMediatorServiceCollectionExtensions.AddUOMediatorGeneratedRoutes(services, configure);");
        source.AppendLine("        var registrationState = global::UO.Mediator.UOMediatorServiceCollectionExtensions.CreateGeneratedRegistrationState(services);");

        foreach (var handler in handlers)
        {
            var request = handler.RequestType.ToDisplayString(TypeFormat);
            var response = handler.ResponseType.ToDisplayString(TypeFormat);
            var implementation = handler.ImplementationType.ToDisplayString(TypeFormat);
            var registrationMethod = handler.HasGeneratedRoute
                ? "AddGeneratedRoutedRequest"
                : "AddGeneratedRequest";
            if (handler.HasResponse)
            {
                source.AppendLine($"        global::UO.Mediator.UOMediatorServiceCollectionExtensions.{registrationMethod}<");
                source.AppendLine($"            {request},");
                source.AppendLine($"            {response},");
                source.AppendLine($"            {implementation}>(services, registrationState);");
            }
            else
            {
                source.AppendLine($"        global::UO.Mediator.UOMediatorServiceCollectionExtensions.{registrationMethod}<");
                source.AppendLine($"            {request},");
                source.AppendLine($"            {implementation}>(services, registrationState);");
            }

            source.AppendLine("        global::UO.Mediator.UOMediatorServiceCollectionExtensions.AddGeneratedLoggingBehavior<");
            source.AppendLine($"            {request},");
            source.AppendLine($"            {response}>(services, registrationState);");
        }

        foreach (var behavior in behaviors)
        {
            source.AppendLine("        global::UO.Mediator.UOMediatorServiceCollectionExtensions.AddGeneratedBehavior<");
            source.AppendLine($"            {behavior.RequestType.ToDisplayString(TypeFormat)},");
            source.AppendLine($"            {behavior.ResponseType.ToDisplayString(TypeFormat)},");
            source.AppendLine($"            {behavior.ImplementationType.ToDisplayString(TypeFormat)}>(services, registrationState);");
        }

        source.AppendLine("        return services;");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static IReadOnlyList<RequestRouteModel> CreateRoutableRequests(
        IReadOnlyList<INamedTypeSymbol> assemblyTypes,
        INamedTypeSymbol requestType,
        INamedTypeSymbol genericRequestType,
        INamedTypeSymbol unitType)
    {
        var routes = new List<RequestRouteModel>();
        foreach (var type in assemblyTypes)
        {
            if (type.ContainingType is not null ||
                type.IsAbstract ||
                type.IsGenericType ||
                type.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
                !IsPartial(type))
            {
                continue;
            }

            var isNoResponse = type.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate, requestType));
            var responses = type.AllInterfaces
                .Where(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    genericRequestType))
                .Select(candidate => candidate.TypeArguments[0])
                .Where(response =>
                    !isNoResponse ||
                    !SymbolEqualityComparer.Default.Equals(response, unitType))
                .ToArray();

            if (isNoResponse || responses.Length > 0)
            {
                routes.Add(new RequestRouteModel(type, isNoResponse, responses));
            }
        }

        return routes;
    }

    private static string RenderRequestRoutes(IReadOnlyList<RequestRouteModel> routes)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");

        foreach (var route in routes)
        {
            var request = route.RequestType.ToDisplayString(TypeFormat);
            var namespaceName = route.RequestType.ContainingNamespace.ToDisplayString();
            var declaration = GetPartialDeclaration(route.RequestType);
            var interfaces = new List<string>();
            if (route.IsNoResponse)
            {
                interfaces.Add("global::UO.Mediator.Dispatching.IGeneratedRequestRoute");
                interfaces.Add(
                    "global::UO.Mediator.Dispatching.IGeneratedRequestRoute<" +
                    "global::UO.Mediator.Dispatching.Unit>");
            }

            interfaces.AddRange(route.ResponseTypes.Select(response =>
                "global::UO.Mediator.Dispatching.IGeneratedRequestRoute<" +
                response.ToDisplayString(TypeFormat) + ">"));

            if (!route.RequestType.ContainingNamespace.IsGlobalNamespace)
            {
                source.AppendLine($"namespace {namespaceName}");
                source.AppendLine("{");
            }

            var indent = route.RequestType.ContainingNamespace.IsGlobalNamespace ? "" : "    ";
            source.AppendLine($"{indent}{declaration} :");
            for (var index = 0; index < interfaces.Count; index++)
            {
                var suffix = index == interfaces.Count - 1 ? "" : ",";
                source.AppendLine($"{indent}    {interfaces[index]}{suffix}");
            }
            source.AppendLine($"{indent}{{");

            if (route.IsNoResponse)
            {
                source.AppendLine($"{indent}    global::System.Threading.Tasks.Task global::UO.Mediator.Dispatching.IGeneratedRequestRoute.DispatchAsync(");
                source.AppendLine($"{indent}        global::UO.Mediator.Dispatching.IGeneratedDispatchContext context) =>");
                source.AppendLine($"{indent}        context.DispatchAsync<{request}>(this);");
                source.AppendLine();
                source.AppendLine($"{indent}    global::System.Threading.Tasks.Task<global::UO.Mediator.Dispatching.Unit> global::UO.Mediator.Dispatching.IGeneratedRequestRoute<global::UO.Mediator.Dispatching.Unit>.DispatchAsync(");
                source.AppendLine($"{indent}        global::UO.Mediator.Dispatching.IGeneratedDispatchContext context) =>");
                source.AppendLine($"{indent}        context.DispatchAsync<{request}, global::UO.Mediator.Dispatching.Unit>(this);");
            }

            foreach (var response in route.ResponseTypes)
            {
                if (route.IsNoResponse)
                {
                    source.AppendLine();
                }

                var responseName = response.ToDisplayString(TypeFormat);
                source.AppendLine($"{indent}    global::System.Threading.Tasks.Task<{responseName}> global::UO.Mediator.Dispatching.IGeneratedRequestRoute<{responseName}>.DispatchAsync(");
                source.AppendLine($"{indent}        global::UO.Mediator.Dispatching.IGeneratedDispatchContext context) =>");
                source.AppendLine($"{indent}        context.DispatchAsync<{request}, {responseName}>(this);");
            }

            source.AppendLine($"{indent}}}");
            if (!route.RequestType.ContainingNamespace.IsGlobalNamespace)
            {
                source.AppendLine("}");
            }
            source.AppendLine();
        }

        return source.ToString();
    }

    private static bool IsPartial(INamedTypeSymbol type)
    {
        return type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is TypeDeclarationSyntax declaration &&
            declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
    }

    private static bool HasGeneratedRoute(
        ITypeSymbol requestType,
        INamedTypeSymbol generatedNoResponseRouteType,
        INamedTypeSymbol generatedResponseRouteType,
        bool hasResponse)
    {
        if (requestType is not INamedTypeSymbol namedRequest)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(
                namedRequest.ContainingAssembly,
                generatedNoResponseRouteType.ContainingAssembly))
        {
            return false;
        }

        if (namedRequest.Locations.Any(static location => location.IsInSource))
        {
            return namedRequest.ContainingType is null &&
                   !namedRequest.IsAbstract &&
                   !namedRequest.IsGenericType &&
                   namedRequest.TypeKind is TypeKind.Class or TypeKind.Struct &&
                   IsPartial(namedRequest);
        }

        return hasResponse
            ? namedRequest.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    generatedResponseRouteType))
            : namedRequest.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate,
                    generatedNoResponseRouteType));
    }

    private static string GetPartialDeclaration(INamedTypeSymbol type)
    {
        if (type.IsRecord)
        {
            return type.TypeKind == TypeKind.Struct
                ? $"partial record struct {type.Name}"
                : $"partial record {type.Name}";
        }

        return type.TypeKind == TypeKind.Struct
            ? $"partial struct {type.Name}"
            : $"partial class {type.Name}";
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            foreach (var candidate in EnumerateTypeAndNestedTypes(type))
            {
                yield return candidate;
            }
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNestedTypes(
        INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nestedType in type.GetTypeMembers())
        {
            foreach (var candidate in EnumerateTypeAndNestedTypes(nestedType))
            {
                yield return candidate;
            }
        }
    }

    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (
                    Accessibility.Public or
                    Accessibility.Internal or
                    Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static string CreateIdentifier(string assemblyName)
    {
        var identifier = new StringBuilder(assemblyName.Length);
        foreach (var character in assemblyName)
        {
            if (character == '_' || char.IsLetterOrDigit(character))
            {
                identifier.Append(character);
            }
        }

        if (identifier.Length == 0)
        {
            return "Assembly";
        }

        if (!SyntaxFacts.IsIdentifierStartCharacter(identifier[0]))
        {
            identifier.Insert(0, '_');
        }

        const string packagePrefix = "UOMediator";
        if (identifier.Length > packagePrefix.Length &&
            identifier.ToString().StartsWith(packagePrefix, StringComparison.Ordinal))
        {
            identifier.Remove(0, packagePrefix.Length);
        }

        return identifier.ToString();
    }

    private sealed class HandlerModelComparer : IEqualityComparer<HandlerModel>
    {
        public static HandlerModelComparer Instance { get; } = new();

        public bool Equals(HandlerModel? x, HandlerModel? y)
        {
            return x is not null && y is not null &&
                   x.HasResponse == y.HasResponse &&
                   SymbolEqualityComparer.Default.Equals(x.RequestType, y.RequestType) &&
                   SymbolEqualityComparer.Default.Equals(x.ResponseType, y.ResponseType) &&
                   SymbolEqualityComparer.Default.Equals(
                       x.ImplementationType,
                       y.ImplementationType);
        }

        public int GetHashCode(HandlerModel obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj.ImplementationType);
        }
    }

    private sealed class BehaviorModelComparer : IEqualityComparer<BehaviorModel>
    {
        public static BehaviorModelComparer Instance { get; } = new();

        public bool Equals(BehaviorModel? x, BehaviorModel? y)
        {
            return x is not null && y is not null &&
                   SymbolEqualityComparer.Default.Equals(x.RequestType, y.RequestType) &&
                   SymbolEqualityComparer.Default.Equals(x.ResponseType, y.ResponseType) &&
                   SymbolEqualityComparer.Default.Equals(
                       x.ImplementationType,
                       y.ImplementationType);
        }

        public int GetHashCode(BehaviorModel obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj.ImplementationType);
        }
    }

    private sealed class HandlerKeyComparer : IEqualityComparer<HandlerKey>
    {
        public static HandlerKeyComparer Instance { get; } = new();

        public bool Equals(HandlerKey? x, HandlerKey? y)
        {
            return x is not null && y is not null &&
                   x.HasResponse == y.HasResponse &&
                   SymbolEqualityComparer.Default.Equals(x.RequestType, y.RequestType) &&
                   SymbolEqualityComparer.Default.Equals(x.ResponseType, y.ResponseType);
        }

        public int GetHashCode(HandlerKey obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj.RequestType);
        }
    }

    private sealed class HandlerModel
    {
        public HandlerModel(
            ITypeSymbol requestType,
            ITypeSymbol responseType,
            INamedTypeSymbol implementationType,
            bool hasResponse,
            bool hasGeneratedRoute)
        {
            RequestType = requestType;
            ResponseType = responseType;
            ImplementationType = implementationType;
            HasResponse = hasResponse;
            HasGeneratedRoute = hasGeneratedRoute;
        }

        public ITypeSymbol RequestType { get; }
        public ITypeSymbol ResponseType { get; }
        public INamedTypeSymbol ImplementationType { get; }
        public bool HasResponse { get; }
        public bool HasGeneratedRoute { get; }
    }

    private sealed class BehaviorModel
    {
        public BehaviorModel(
            ITypeSymbol requestType,
            ITypeSymbol responseType,
            INamedTypeSymbol implementationType)
        {
            RequestType = requestType;
            ResponseType = responseType;
            ImplementationType = implementationType;
        }

        public ITypeSymbol RequestType { get; }
        public ITypeSymbol ResponseType { get; }
        public INamedTypeSymbol ImplementationType { get; }
    }

    private sealed class HandlerKey
    {
        public HandlerKey(
            ITypeSymbol requestType,
            ITypeSymbol responseType,
            bool hasResponse)
        {
            RequestType = requestType;
            ResponseType = responseType;
            HasResponse = hasResponse;
        }

        public ITypeSymbol RequestType { get; }
        public ITypeSymbol ResponseType { get; }
        public bool HasResponse { get; }
    }

    private sealed class RequestRouteModel
    {
        public RequestRouteModel(
            INamedTypeSymbol requestType,
            bool isNoResponse,
            IReadOnlyList<ITypeSymbol> responseTypes)
        {
            RequestType = requestType;
            IsNoResponse = isNoResponse;
            ResponseTypes = responseTypes;
        }

        public INamedTypeSymbol RequestType { get; }
        public bool IsNoResponse { get; }
        public IReadOnlyList<ITypeSymbol> ResponseTypes { get; }
    }
}

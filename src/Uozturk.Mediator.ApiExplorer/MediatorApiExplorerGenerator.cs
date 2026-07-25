using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Uozturk.Mediator.ApiExplorer.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class MediatorApiExplorerGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName =
        "Uozturk.Mediator.ApiExplorer.MediatorApiExplorerAttribute";
    private const string GenericRequestMetadataName =
        "Uozturk.Mediator.Dispatching.IRequest`1";
    private const string UnitMetadataName =
        "Uozturk.Mediator.Dispatching.Unit";
    private const string DispatcherMetadataName =
        "Uozturk.Mediator.Dispatching.IRequestDispatcher";
    private const string ControllerBaseMetadataName =
        "Microsoft.AspNetCore.Mvc.ControllerBase";
    private const string DefaultRootPath = "/api/app";
    private const string RootPathBuildProperty =
        "build_property.UozturkMediatorApiRootPath";

    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly DiagnosticDescriptor MissingDependency = new(
        "UOMA001",
        "ApiExplorer dependency is missing",
        "Uozturk.Mediator.ApiExplorer could not find required type '{0}' in the API host compilation",
        "Uozturk.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidRequest = new(
        "UOMA002",
        "Unsupported mediator request",
        "Type '{0}' is marked with MediatorApiExplorer but {1}",
        "Uozturk.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidRoute = new(
        "UOMA003",
        "Invalid mediator API route",
        "The API route for request '{0}' is invalid: {1}",
        "Uozturk.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateRoute = new(
        "UOMA004",
        "Duplicate mediator API route",
        "Request '{0}' conflicts with request '{1}' at {2} {3}",
        "Uozturk.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidAuthorization = new(
        "UOMA005",
        "Invalid mediator API authorization",
        "Request '{0}' cannot set both AuthorizationPolicy and AllowAnonymous",
        "Uozturk.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidHttpMethod = new(
        "UOMA006",
        "Invalid mediator API HTTP method",
        "Request '{0}' specifies unsupported MediatorHttpMethod value '{1}'",
        "Uozturk.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var rootPath = context.AnalyzerConfigOptionsProvider.Select(
            static (options, _) =>
            {
                return options.GlobalOptions.TryGetValue(RootPathBuildProperty, out var value)
                    ? value
                    : DefaultRootPath;
            });

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(rootPath),
            static (productionContext, input) =>
                Generate(productionContext, input.Left, input.Right));
    }

    private static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        string rootPath)
    {
        var attributeType = compilation.GetTypeByMetadataName(AttributeMetadataName);
        var genericRequestType = compilation.GetTypeByMetadataName(GenericRequestMetadataName);
        var unitType = compilation.GetTypeByMetadataName(UnitMetadataName);
        var dispatcherType = compilation.GetTypeByMetadataName(DispatcherMetadataName);
        var controllerBaseType = compilation.GetTypeByMetadataName(ControllerBaseMetadataName);

        if (!TryRequireType(context, attributeType, AttributeMetadataName) |
            !TryRequireType(context, genericRequestType, GenericRequestMetadataName) |
            !TryRequireType(context, unitType, UnitMetadataName) |
            !TryRequireType(context, dispatcherType, DispatcherMetadataName) |
            !TryRequireType(context, controllerBaseType, ControllerBaseMetadataName))
        {
            return;
        }

        if (!TryNormalizeRootPath(rootPath, out var normalizedRootPath, out var rootPathError))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidRoute,
                Location.None,
                "<API host>",
                rootPathError));
            return;
        }

        var requests = FindAttributedTypes(
                compilation,
                attributeType!,
                genericRequestType!.ContainingAssembly)
            .OrderBy(
                static type => type.ToDisplayString(FullyQualifiedTypeFormat),
                StringComparer.Ordinal)
            .ToArray();

        var endpoints = new List<EndpointModel>(requests.Length);
        foreach (var request in requests)
        {
            var endpoint = TryCreateEndpoint(
                context,
                request,
                attributeType!,
                genericRequestType!,
                unitType!,
                normalizedRootPath);

            if (endpoint is not null)
            {
                endpoints.Add(endpoint);
            }
        }

        var conflictingEndpoints = FindAndReportRouteConflicts(context, endpoints);
        foreach (var endpoint in endpoints)
        {
            if (conflictingEndpoints.Contains(endpoint))
            {
                continue;
            }

            context.AddSource(endpoint.HintName, RenderController(endpoint));
        }
    }

    private static bool TryRequireType(
        SourceProductionContext context,
        INamedTypeSymbol? type,
        string metadataName)
    {
        if (type is not null)
        {
            return true;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            MissingDependency,
            Location.None,
            metadataName));
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> FindAttributedTypes(
        Compilation compilation,
        INamedTypeSymbol attributeType,
        IAssemblySymbol mediatorAssembly)
    {
        var assemblies = new List<IAssemblySymbol>
        {
            compilation.Assembly
        };

        assemblies.AddRange(compilation.SourceModule.ReferencedAssemblySymbols);

        foreach (var assembly in assemblies
                     .Where(assembly =>
                         SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly) ||
                         ReferencesAssembly(assembly, mediatorAssembly)))
        {
            foreach (var type in GetTypes(assembly.GlobalNamespace))
            {
                if (GetExplorerAttribute(type, attributeType) is not null)
                {
                    yield return type;
                }
            }
        }
    }

    private static bool ReferencesAssembly(
        IAssemblySymbol assembly,
        IAssemblySymbol expectedReference)
    {
        foreach (var module in assembly.Modules)
        {
            if (module.ReferencedAssemblySymbols.Any(reference =>
                    SymbolEqualityComparer.Default.Equals(reference, expectedReference)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            foreach (var nestedType in GetTypes(type))
            {
                yield return nestedType;
            }
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in GetTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetTypes(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nestedType in type.GetTypeMembers())
        {
            foreach (var descendant in GetTypes(nestedType))
            {
                yield return descendant;
            }
        }
    }

    private static AttributeData? GetExplorerAttribute(
        INamedTypeSymbol type,
        INamedTypeSymbol attributeType)
    {
        return type.GetAttributes().FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
    }

    private static EndpointModel? TryCreateEndpoint(
        SourceProductionContext context,
        INamedTypeSymbol requestType,
        INamedTypeSymbol attributeType,
        INamedTypeSymbol genericRequestType,
        INamedTypeSymbol unitType,
        string rootPath)
    {
        var location = GetLocation(requestType, attributeType);
        var requestName = requestType.ToDisplayString(FullyQualifiedTypeFormat);

        if (requestType.TypeKind != TypeKind.Class)
        {
            ReportInvalidRequest(context, location, requestName, "it is not a class or record class");
            return null;
        }

        if (requestType.IsAbstract)
        {
            ReportInvalidRequest(context, location, requestName, "it is abstract");
            return null;
        }

        if (!IsPublic(requestType))
        {
            ReportInvalidRequest(
                context,
                location,
                requestName,
                "it or one of its containing types is not public");
            return null;
        }

        if (IsGeneric(requestType))
        {
            ReportInvalidRequest(
                context,
                location,
                requestName,
                "it or one of its containing types is generic");
            return null;
        }

        var responseTypes = new List<ITypeSymbol>();
        foreach (var interfaceType in requestType.AllInterfaces)
        {
            if (!interfaceType.IsGenericType ||
                !SymbolEqualityComparer.Default.Equals(
                    interfaceType.OriginalDefinition,
                    genericRequestType))
            {
                continue;
            }

            var responseCandidate = interfaceType.TypeArguments[0];
            if (!responseTypes.Any(existing =>
                    SymbolEqualityComparer.Default.Equals(existing, responseCandidate)))
            {
                responseTypes.Add(responseCandidate);
            }
        }

        if (responseTypes.Count == 0)
        {
            ReportInvalidRequest(
                context,
                location,
                requestName,
                "it does not implement IRequest or IRequest<TResponse>");
            return null;
        }

        if (responseTypes.Count > 1)
        {
            ReportInvalidRequest(
                context,
                location,
                requestName,
                "it implements more than one IRequest<TResponse> contract");
            return null;
        }

        var attribute = GetExplorerAttribute(requestType, attributeType)!;
        var settings = ReadSettings(attribute);
        if (settings.AllowAnonymous && settings.AuthorizationPolicy is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidAuthorization,
                location,
                requestName));
            return null;
        }

        if (settings.AuthorizationPolicy is not null &&
            string.IsNullOrWhiteSpace(settings.AuthorizationPolicy))
        {
            ReportInvalidRequest(
                context,
                location,
                requestName,
                "AuthorizationPolicy cannot be empty");
            return null;
        }

        var convention = GetConvention(requestType.Name);
        if (!TryGetHttpMethod(
                settings.HttpMethodValue,
                convention.HttpMethod,
                out var httpMethod))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidHttpMethod,
                location,
                requestName,
                settings.HttpMethodValue.ToString(CultureInfo.InvariantCulture)));
            return null;
        }

        string route;
        if (settings.Route is not null)
        {
            route = settings.Route;
        }
        else
        {
            var resourceName = ToKebabCase(convention.ResourceName);
            if (resourceName.Length == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidRoute,
                    location,
                    requestName,
                    "the convention produced an empty resource name; set Route explicitly"));
                return null;
            }

            route = CombineRoute(rootPath, resourceName);
        }

        if (!TryValidateAbsoluteRoute(route, out var routeError))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidRoute,
                location,
                requestName,
                routeError));
            return null;
        }

        var responseType = responseTypes[0];
        var stableIdentity = requestType.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        var hash = ComputeStableHash(stableIdentity);
        var controllerName = "Mediator_" +
                             SanitizeIdentifier(requestType.Name) +
                             "_" +
                             hash +
                             "Controller";

        return new EndpointModel(
            requestType,
            requestName,
            responseType.ToDisplayString(FullyQualifiedTypeFormat),
            SymbolEqualityComparer.Default.Equals(responseType, unitType),
            httpMethod,
            route,
            controllerName,
            controllerName + ".g.cs",
            settings.AuthorizationPolicy,
            settings.AllowAnonymous,
            location);
    }

    private static void ReportInvalidRequest(
        SourceProductionContext context,
        Location location,
        string requestName,
        string reason)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            InvalidRequest,
            location,
            requestName,
            reason));
    }

    private static bool IsPublic(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Location GetLocation(
        INamedTypeSymbol requestType,
        INamedTypeSymbol attributeType)
    {
        var attribute = GetExplorerAttribute(requestType, attributeType);
        return attribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation() ??
               requestType.Locations.FirstOrDefault() ??
               Location.None;
    }

    private static EndpointSettings ReadSettings(AttributeData attribute)
    {
        string? route = null;
        string? authorizationPolicy = null;
        var allowAnonymous = false;
        var httpMethodValue = 0;

        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case "Route":
                    route = argument.Value.Value as string;
                    break;
                case "HttpMethod" when argument.Value.Value is int value:
                    httpMethodValue = value;
                    break;
                case "AuthorizationPolicy":
                    authorizationPolicy = argument.Value.Value as string;
                    break;
                case "AllowAnonymous" when argument.Value.Value is bool value:
                    allowAnonymous = value;
                    break;
            }
        }

        return new EndpointSettings(
            route,
            httpMethodValue,
            authorizationPolicy,
            allowAnonymous);
    }

    private static ConventionResult GetConvention(string typeName)
    {
        var nameWithoutSuffix = RemoveSuffix(typeName);
        var conventions = new[]
        {
            new NameConvention("GetList", "GET"),
            new NameConvention("GetAll", "GET"),
            new NameConvention("Get", "GET"),
            new NameConvention("Put", "PUT"),
            new NameConvention("Update", "PUT"),
            new NameConvention("Delete", "DELETE"),
            new NameConvention("Remove", "DELETE"),
            new NameConvention("Create", "POST"),
            new NameConvention("Add", "POST"),
            new NameConvention("Insert", "POST"),
            new NameConvention("Post", "POST"),
            new NameConvention("Patch", "PATCH")
        };

        foreach (var convention in conventions)
        {
            if (nameWithoutSuffix.StartsWith(
                    convention.Prefix,
                    StringComparison.Ordinal))
            {
                return new ConventionResult(
                    convention.HttpMethod,
                    nameWithoutSuffix.Substring(convention.Prefix.Length));
            }
        }

        return new ConventionResult("POST", nameWithoutSuffix);
    }

    private static string RemoveSuffix(string typeName)
    {
        var suffixes = new[]
        {
            "Request",
            "Command",
            "Query"
        };

        foreach (var suffix in suffixes)
        {
            if (typeName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return typeName.Substring(0, typeName.Length - suffix.Length);
            }
        }

        return typeName;
    }

    private static bool TryGetHttpMethod(
        int configuredMethod,
        string conventionMethod,
        out string httpMethod)
    {
        switch (configuredMethod)
        {
            case 0:
                httpMethod = conventionMethod;
                return true;
            case 1:
                httpMethod = "GET";
                return true;
            case 2:
                httpMethod = "POST";
                return true;
            case 3:
                httpMethod = "PUT";
                return true;
            case 4:
                httpMethod = "DELETE";
                return true;
            case 5:
                httpMethod = "PATCH";
                return true;
            default:
                httpMethod = string.Empty;
                return false;
        }
    }

    private static bool TryNormalizeRootPath(
        string? rootPath,
        out string normalizedRootPath,
        out string error)
    {
        var value = string.IsNullOrWhiteSpace(rootPath)
            ? DefaultRootPath
            : rootPath!.Trim();

        if (!TryValidateAbsoluteRoute(value, out error))
        {
            normalizedRootPath = string.Empty;
            return false;
        }

        normalizedRootPath = value.Length > 1
            ? value.TrimEnd('/')
            : value;
        return true;
    }

    private static bool TryValidateAbsoluteRoute(string route, out string error)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            error = "the route cannot be empty";
            return false;
        }

        if (!route.StartsWith("/", StringComparison.Ordinal))
        {
            error = "the route must be absolute and start with '/'";
            return false;
        }

        if (route.Length > 1 && route.EndsWith("/", StringComparison.Ordinal))
        {
            error = "the route cannot end with '/'";
            return false;
        }

        if (route.IndexOf("//", StringComparison.Ordinal) >= 0)
        {
            error = "the route cannot contain empty path segments";
            return false;
        }

        if (route.IndexOf('{') >= 0 || route.IndexOf('}') >= 0)
        {
            error = "route parameters are not supported in version 1";
            return false;
        }

        if (route.IndexOf('?') >= 0 || route.IndexOf('#') >= 0)
        {
            error = "query strings and fragments are not part of an endpoint route";
            return false;
        }

        if (route.Any(char.IsWhiteSpace))
        {
            error = "the route cannot contain whitespace";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string CombineRoute(string rootPath, string resourceName)
    {
        return rootPath == "/"
            ? "/" + resourceName
            : rootPath + "/" + resourceName;
    }

    private static string ToKebabCase(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (!char.IsLetterOrDigit(current))
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }

                continue;
            }

            if (char.IsUpper(current) &&
                index > 0 &&
                builder.Length > 0 &&
                builder[builder.Length - 1] != '-' &&
                (char.IsLower(value[index - 1]) ||
                 char.IsDigit(value[index - 1]) ||
                 (char.IsUpper(value[index - 1]) &&
                  index + 1 < value.Length &&
                  char.IsLower(value[index + 1]))))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString().Trim('-');
    }

    private static HashSet<EndpointModel> FindAndReportRouteConflicts(
        SourceProductionContext context,
        IReadOnlyCollection<EndpointModel> endpoints)
    {
        var conflicts = new HashSet<EndpointModel>();

        foreach (var group in endpoints.GroupBy(
                     static endpoint => endpoint.HttpMethod + " " + endpoint.Route,
                     StringComparer.OrdinalIgnoreCase))
        {
            var matchingEndpoints = group.ToArray();
            if (matchingEndpoints.Length < 2)
            {
                continue;
            }

            foreach (var endpoint in matchingEndpoints)
            {
                var other = matchingEndpoints.First(candidate =>
                    !ReferenceEquals(candidate, endpoint));
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateRoute,
                    endpoint.Location,
                    endpoint.RequestDisplayName,
                    other.RequestDisplayName,
                    endpoint.HttpMethod,
                    endpoint.Route));
                conflicts.Add(endpoint);
            }
        }

        return conflicts;
    }

    private static string RenderController(EndpointModel endpoint)
    {
        var requestTypeName = endpoint.RequestType.ToDisplayString(FullyQualifiedTypeFormat);
        var bindingAttribute = endpoint.HttpMethod == "GET" ||
                               endpoint.HttpMethod == "DELETE"
            ? "FromQueryAttribute"
            : "FromBodyAttribute";
        var httpAttribute = "Http" +
                            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                                endpoint.HttpMethod.ToLowerInvariant()) +
                            "Attribute";

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("namespace Uozturk.Mediator.ApiExplorer.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    [global::Microsoft.AspNetCore.Mvc.ApiControllerAttribute]");

        if (endpoint.AllowAnonymous)
        {
            builder.AppendLine(
                "    [global::Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute]");
        }
        else if (endpoint.AuthorizationPolicy is not null)
        {
            builder.Append("    [global::Microsoft.AspNetCore.Authorization.AuthorizeAttribute(Policy = \"")
                .Append(EscapeCSharpString(endpoint.AuthorizationPolicy))
                .AppendLine("\")]");
        }

        builder.Append("    public sealed class ")
            .Append(endpoint.ControllerName)
            .AppendLine(" : global::Microsoft.AspNetCore.Mvc.ControllerBase");
        builder.AppendLine("    {");
        builder.AppendLine(
            "        private readonly global::Uozturk.Mediator.Dispatching.IRequestDispatcher _dispatcher;");
        builder.AppendLine();
        builder.Append("        public ")
            .Append(endpoint.ControllerName)
            .AppendLine("(");
        builder.AppendLine(
            "            global::Uozturk.Mediator.Dispatching.IRequestDispatcher dispatcher)");
        builder.AppendLine("        {");
        builder.AppendLine("            _dispatcher = dispatcher;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.Append("        [global::Microsoft.AspNetCore.Mvc.")
            .Append(httpAttribute)
            .Append("(\"")
            .Append(EscapeCSharpString(endpoint.Route))
            .AppendLine("\")]");

        if (endpoint.IsUnit)
        {
            builder.AppendLine(
                "        public async global::System.Threading.Tasks.Task<global::Microsoft.AspNetCore.Mvc.IActionResult> HandleAsync(");
        }
        else
        {
            builder.Append(
                    "        public async global::System.Threading.Tasks.Task<global::Microsoft.AspNetCore.Mvc.ActionResult<")
                .Append(endpoint.ResponseTypeName)
                .AppendLine(">> HandleAsync(");
        }

        builder.Append("            [global::Microsoft.AspNetCore.Mvc.")
            .Append(bindingAttribute)
            .Append("] ")
            .Append(requestTypeName)
            .AppendLine(" request)");
        builder.AppendLine("        {");

        if (endpoint.IsUnit)
        {
            builder.Append("            await _dispatcher.DispatchAsync<")
                .Append(endpoint.ResponseTypeName)
                .AppendLine(">(request);");
            builder.AppendLine("            return NoContent();");
        }
        else
        {
            builder.Append("            return new global::Microsoft.AspNetCore.Mvc.JsonResult(await _dispatcher.DispatchAsync<")
                .Append(endpoint.ResponseTypeName)
                .AppendLine(">(request));");
        }

        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string EscapeCSharpString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_'
                ? character
                : '_');
        }

        return builder.ToString();
    }

    private static string ComputeStableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;

        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    private sealed class EndpointModel
    {
        public EndpointModel(
            INamedTypeSymbol requestType,
            string requestDisplayName,
            string responseTypeName,
            bool isUnit,
            string httpMethod,
            string route,
            string controllerName,
            string hintName,
            string? authorizationPolicy,
            bool allowAnonymous,
            Location location)
        {
            RequestType = requestType;
            RequestDisplayName = requestDisplayName;
            ResponseTypeName = responseTypeName;
            IsUnit = isUnit;
            HttpMethod = httpMethod;
            Route = route;
            ControllerName = controllerName;
            HintName = hintName;
            AuthorizationPolicy = authorizationPolicy;
            AllowAnonymous = allowAnonymous;
            Location = location;
        }

        public INamedTypeSymbol RequestType { get; }

        public string RequestDisplayName { get; }

        public string ResponseTypeName { get; }

        public bool IsUnit { get; }

        public string HttpMethod { get; }

        public string Route { get; }

        public string ControllerName { get; }

        public string HintName { get; }

        public string? AuthorizationPolicy { get; }

        public bool AllowAnonymous { get; }

        public Location Location { get; }
    }

    private sealed class EndpointSettings
    {
        public EndpointSettings(
            string? route,
            int httpMethodValue,
            string? authorizationPolicy,
            bool allowAnonymous)
        {
            Route = route;
            HttpMethodValue = httpMethodValue;
            AuthorizationPolicy = authorizationPolicy;
            AllowAnonymous = allowAnonymous;
        }

        public string? Route { get; }

        public int HttpMethodValue { get; }

        public string? AuthorizationPolicy { get; }

        public bool AllowAnonymous { get; }
    }

    private sealed class ConventionResult
    {
        public ConventionResult(string httpMethod, string resourceName)
        {
            HttpMethod = httpMethod;
            ResourceName = resourceName;
        }

        public string HttpMethod { get; }

        public string ResourceName { get; }
    }

    private sealed class NameConvention
    {
        public NameConvention(string prefix, string httpMethod)
        {
            Prefix = prefix;
            HttpMethod = httpMethod;
        }

        public string Prefix { get; }

        public string HttpMethod { get; }
    }
}

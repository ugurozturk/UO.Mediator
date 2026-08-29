using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UO.Mediator.ApiExplorer.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class MediatorApiExplorerGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName =
        "UO.Mediator.ApiExplorer.MediatorApiExplorerAttribute";
    private const string AttributeUsageMetadataName =
        "System.AttributeUsageAttribute";
    private const string AllowAnonymousAttributeMetadataName =
        "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";
    private const string GenericRequestMetadataName =
        "UO.Mediator.Dispatching.IRequest`1";
    private const string UnitMetadataName =
        "UO.Mediator.Dispatching.Unit";
    private const string DispatcherMetadataName =
        "UO.Mediator.Dispatching.IRequestDispatcher";
    private const string ControllerBaseMetadataName =
        "Microsoft.AspNetCore.Mvc.ControllerBase";
    private const string DefaultControllerBase = ControllerBaseMetadataName;
    private const string DefaultRootPath = "/api/app";
    private const string RootPathBuildProperty =
        "build_property.UOMediatorApiRootPath";
    private const string ControllerPrefixBuildProperty =
        "build_property.UOMediatorControllerPrefix";
    private const string ControllerSuffixBuildProperty =
        "build_property.UOMediatorControllerSuffix";
    private const string ControllerBaseBuildProperty =
        "build_property.UOMediatorControllerBase";

    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor MissingDependency = new(
        "UOMA001",
        "ApiExplorer dependency is missing",
        "UO.Mediator.ApiExplorer could not find required type '{0}' in the API host compilation",
        "UO.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidRequest = new(
        "UOMA002",
        "Unsupported mediator request",
        "Type '{0}' is marked with MediatorApiExplorer but {1}",
        "UO.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidRoute = new(
        "UOMA003",
        "Invalid mediator API route",
        "The API route for request '{0}' is invalid: {1}",
        "UO.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateRoute = new(
        "UOMA004",
        "Duplicate mediator API route",
        "Request '{0}' conflicts with request '{1}' at {2} {3}",
        "UO.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidAuthorization = new(
        "UOMA005",
        "Invalid mediator API authorization",
        "Request '{0}' cannot set both AuthorizationPolicy and AllowAnonymous",
        "UO.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidHttpMethod = new(
        "UOMA006",
        "Invalid mediator API HTTP method",
        "Request '{0}' specifies unsupported MediatorHttpMethod value '{1}'",
        "UO.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidController = new(
        "UOMA007",
        "Invalid generated controller configuration",
        "The generated controller for request '{0}' is invalid: {1}",
        "UO.Mediator.ApiExplorer",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.AnalyzerConfigOptionsProvider.Select(
            static (options, _) =>
            {
                return new GeneratorOptions(
                    GetGlobalOption(
                        options,
                        RootPathBuildProperty,
                        DefaultRootPath),
                    GetGlobalOption(
                        options,
                        ControllerPrefixBuildProperty,
                        string.Empty),
                    GetGlobalOption(
                        options,
                        ControllerSuffixBuildProperty,
                        string.Empty),
                    GetGlobalOption(
                        options,
                        ControllerBaseBuildProperty,
                        DefaultControllerBase));
            });

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(options),
            static (productionContext, input) =>
                Generate(productionContext, input.Left, input.Right));
    }

    private static string GetGlobalOption(
        AnalyzerConfigOptionsProvider options,
        string key,
        string defaultValue)
    {
        return options.GlobalOptions.TryGetValue(key, out var value)
            ? value
            : defaultValue;
    }

    private static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        GeneratorOptions options)
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

        var configuredControllerBaseOption = options.ControllerBase.Length == 0
            ? DefaultControllerBase
            : options.ControllerBase;
        var configuredControllerBaseName = configuredControllerBaseOption.Trim();
        if (configuredControllerBaseName.Length == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                Location.None,
                "<API host>",
                "UOMediatorControllerBase cannot be empty"));
            return;
        }

        if (configuredControllerBaseName.StartsWith(
                "global::",
                StringComparison.Ordinal))
        {
            configuredControllerBaseName = configuredControllerBaseName.Substring(
                "global::".Length);
        }

        var configuredControllerBaseType =
            compilation.GetTypeByMetadataName(configuredControllerBaseName);
        if (configuredControllerBaseType is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                Location.None,
                "<API host>",
                $"UOMediatorControllerBase type '{configuredControllerBaseOption}' " +
                "could not be found"));
            return;
        }

        if (configuredControllerBaseType.IsSealed)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                Location.None,
                "<API host>",
                $"UOMediatorControllerBase type '{configuredControllerBaseOption}' " +
                "cannot be sealed"));
            return;
        }

        if (!InheritsFromOrEquals(configuredControllerBaseType, controllerBaseType!))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                Location.None,
                "<API host>",
                $"UOMediatorControllerBase type '{configuredControllerBaseOption}' " +
                $"must inherit from '{ControllerBaseMetadataName}'"));
            return;
        }

        if (!TryNormalizeRootPath(
                options.RootPath,
                out var normalizedRootPath,
                out var rootPathError))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidRoute,
                Location.None,
                "<API host>",
                rootPathError));
            return;
        }

        if (!TryValidateControllerFragment(
                options.ControllerPrefix,
                allowEmpty: true,
                out var controllerPrefixError))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                Location.None,
                "<API host>",
                $"UOMediatorControllerPrefix {controllerPrefixError}"));
            return;
        }

        if (!TryValidateControllerFragment(
                options.ControllerSuffix,
                allowEmpty: true,
                out var controllerSuffixError))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                Location.None,
                "<API host>",
                $"UOMediatorControllerSuffix {controllerSuffixError}"));
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
                normalizedRootPath,
                options.ControllerPrefix,
                options.ControllerSuffix);

            if (endpoint is not null)
            {
                endpoints.Add(endpoint);
            }
        }

        var conflictingEndpoints = FindAndReportRouteConflicts(context, endpoints);
        var validEndpoints = endpoints
            .Where(endpoint => !conflictingEndpoints.Contains(endpoint))
            .ToArray();

        foreach (var controllerGroup in validEndpoints
                     .GroupBy(
                         static endpoint => endpoint.ControllerName,
                         StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            context.AddSource(
                controllerGroup.Key + ".g.cs",
                RenderController(
                    controllerGroup.Key,
                    configuredControllerBaseType.ToDisplayString(FullyQualifiedTypeFormat),
                    controllerGroup
                        .OrderBy(
                            static endpoint => endpoint.ActionName,
                            StringComparer.Ordinal)
                        .ThenBy(
                            static endpoint => endpoint.RequestDisplayName,
                            StringComparer.Ordinal)
                        .ToArray()));
        }
    }

    private static bool InheritsFromOrEquals(
        INamedTypeSymbol type,
        INamedTypeSymbol expectedBaseType)
    {
        for (INamedTypeSymbol? current = type;
             current is not null;
             current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expectedBaseType))
            {
                return true;
            }
        }

        return false;
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
        string rootPath,
        string controllerPrefix,
        string controllerSuffix)
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
        var controllerGroupName = settings.ControllerName ?? convention.ResourceName;
        if (!TryValidateControllerFragment(
                controllerGroupName,
                allowEmpty: false,
                out var controllerNameError))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                location,
                requestName,
                $"ControllerName {controllerNameError}"));
            return null;
        }

        var controllerName = controllerPrefix +
                             controllerGroupName +
                             controllerSuffix +
                             "Controller";
        if (!IsValidIdentifier(controllerName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                location,
                requestName,
                $"'{controllerName}' is not a valid C# controller class name"));
            return null;
        }

        var actionBaseName = RemoveSuffix(requestType.Name);
        if (actionBaseName.Length == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidController,
                location,
                requestName,
                "the request name does not produce a valid action name"));
            return null;
        }

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
        var actionAttributes = GetActionAttributes(
            requestType,
            attributeType,
            settings.AllowAnonymous);
        return new EndpointModel(
            requestType,
            requestName,
            responseType.ToDisplayString(FullyQualifiedTypeFormat),
            SymbolEqualityComparer.Default.Equals(responseType, unitType),
            httpMethod,
            route,
            controllerName,
            actionBaseName + "Async",
            controllerGroupName,
            settings.AuthorizationPolicy,
            settings.AllowAnonymous,
            actionAttributes,
            location);
    }

    private static IReadOnlyList<string> GetActionAttributes(
        INamedTypeSymbol requestType,
        INamedTypeSymbol explorerAttributeType,
        bool allowAnonymous)
    {
        var attributes = new List<string>();

        foreach (var attribute in requestType.GetAttributes())
        {
            if (attribute.AttributeClass is null ||
                SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass,
                    explorerAttributeType) ||
                (allowAnonymous &&
                 HasMetadataName(
                     attribute.AttributeClass,
                     AllowAnonymousAttributeMetadataName)) ||
                IsCompilerServicesAttribute(attribute.AttributeClass) ||
                !CanTargetMethod(attribute.AttributeClass))
            {
                continue;
            }

            if (TryRenderAttribute(attribute, out var renderedAttribute))
            {
                attributes.Add(renderedAttribute);
            }
        }

        return attributes;
    }

    private static bool IsCompilerServicesAttribute(INamedTypeSymbol attributeType)
    {
        return string.Equals(
            attributeType.ContainingNamespace?.ToDisplayString(),
            "System.Runtime.CompilerServices",
            StringComparison.Ordinal);
    }

    private static bool CanTargetMethod(INamedTypeSymbol attributeType)
    {
        for (var current = attributeType;
             current is not null;
             current = current.BaseType)
        {
            var usage = current.GetAttributes().FirstOrDefault(attribute =>
                attribute.AttributeClass is not null &&
                HasMetadataName(
                    attribute.AttributeClass,
                    AttributeUsageMetadataName));
            if (usage is null)
            {
                continue;
            }

            if (usage.ConstructorArguments.Length == 0 ||
                usage.ConstructorArguments[0].Value is null)
            {
                return true;
            }

            var targets = Convert.ToInt64(
                usage.ConstructorArguments[0].Value,
                CultureInfo.InvariantCulture);
            return (targets & (long)AttributeTargets.Method) != 0;
        }

        // AttributeUsage defaults to AttributeTargets.All.
        return true;
    }

    private static bool HasMetadataName(
        INamedTypeSymbol type,
        string metadataName)
    {
        return string.Equals(
            type.ToDisplayString(),
            metadataName,
            StringComparison.Ordinal);
    }

    private static bool TryRenderAttribute(
        AttributeData attribute,
        out string renderedAttribute)
    {
        if (attribute.AttributeClass is null)
        {
            renderedAttribute = string.Empty;
            return false;
        }

        var arguments = new List<string>(
            attribute.ConstructorArguments.Length + attribute.NamedArguments.Length);
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (!TryRenderTypedConstant(argument, out var renderedArgument))
            {
                renderedAttribute = string.Empty;
                return false;
            }

            arguments.Add(renderedArgument);
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (!TryRenderTypedConstant(argument.Value, out var renderedArgument))
            {
                renderedAttribute = string.Empty;
                return false;
            }

            arguments.Add(argument.Key + " = " + renderedArgument);
        }

        var builder = new StringBuilder();
        builder.Append('[')
            .Append(attribute.AttributeClass.ToDisplayString(FullyQualifiedTypeFormat));
        if (arguments.Count > 0)
        {
            builder.Append('(')
                .Append(string.Join(", ", arguments))
                .Append(')');
        }

        builder.Append(']');
        renderedAttribute = builder.ToString();
        return true;
    }

    private static bool TryRenderTypedConstant(
        TypedConstant constant,
        out string renderedConstant)
    {
        if (constant.Kind == TypedConstantKind.Error)
        {
            renderedConstant = string.Empty;
            return false;
        }

        if (constant.IsNull)
        {
            renderedConstant = "null";
            return true;
        }

        if (constant.Kind == TypedConstantKind.Array)
        {
            if (constant.Type is null)
            {
                renderedConstant = string.Empty;
                return false;
            }

            var elements = new List<string>(constant.Values.Length);
            foreach (var element in constant.Values)
            {
                if (!TryRenderTypedConstant(element, out var renderedElement))
                {
                    renderedConstant = string.Empty;
                    return false;
                }

                elements.Add(renderedElement);
            }

            renderedConstant = "new " +
                               constant.Type.ToDisplayString(FullyQualifiedTypeFormat) +
                               " { " +
                               string.Join(", ", elements) +
                               " }";
            return true;
        }

        if (constant.Kind == TypedConstantKind.Type &&
            constant.Value is ITypeSymbol typeValue)
        {
            renderedConstant = "typeof(" +
                               typeValue.ToDisplayString(FullyQualifiedTypeFormat) +
                               ")";
            return true;
        }

        if (constant.Kind == TypedConstantKind.Enum && constant.Type is not null)
        {
            renderedConstant = "(" +
                               constant.Type.ToDisplayString(FullyQualifiedTypeFormat) +
                               ")" +
                               RenderPrimitive(constant.Value);
            return true;
        }

        renderedConstant = RenderPrimitive(constant.Value);
        return true;
    }

    private static string RenderPrimitive(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string stringValue:
                return "\"" + EscapeCSharpString(stringValue) + "\"";
            case char charValue:
                return "'" + EscapeCSharpChar(charValue) + "'";
            case bool boolValue:
                return boolValue ? "true" : "false";
            case uint uintValue:
                return uintValue.ToString(CultureInfo.InvariantCulture) + "U";
            case long longValue:
                return longValue.ToString(CultureInfo.InvariantCulture) + "L";
            case ulong ulongValue:
                return ulongValue.ToString(CultureInfo.InvariantCulture) + "UL";
            case float floatValue when float.IsNaN(floatValue):
                return "global::System.Single.NaN";
            case float floatValue when float.IsPositiveInfinity(floatValue):
                return "global::System.Single.PositiveInfinity";
            case float floatValue when float.IsNegativeInfinity(floatValue):
                return "global::System.Single.NegativeInfinity";
            case float floatValue:
                return floatValue.ToString("R", CultureInfo.InvariantCulture) + "F";
            case double doubleValue when double.IsNaN(doubleValue):
                return "global::System.Double.NaN";
            case double doubleValue when double.IsPositiveInfinity(doubleValue):
                return "global::System.Double.PositiveInfinity";
            case double doubleValue when double.IsNegativeInfinity(doubleValue):
                return "global::System.Double.NegativeInfinity";
            case double doubleValue:
                return doubleValue.ToString("R", CultureInfo.InvariantCulture) + "D";
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        }
    }

    private static string EscapeCSharpChar(char value)
    {
        switch (value)
        {
            case '\\': return "\\\\";
            case '\'': return "\\'";
            case '\0': return "\\0";
            case '\a': return "\\a";
            case '\b': return "\\b";
            case '\f': return "\\f";
            case '\n': return "\\n";
            case '\r': return "\\r";
            case '\t': return "\\t";
            case '\v': return "\\v";
            default:
                return char.IsControl(value)
                    ? "\\u" + ((int)value).ToString("X4", CultureInfo.InvariantCulture)
                    : value.ToString();
        }
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
        string? controllerName = null;
        string? route = null;
        string? authorizationPolicy = null;
        var allowAnonymous = false;
        var httpMethodValue = 0;

        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case "ControllerName":
                    controllerName = argument.Value.Value as string;
                    break;
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
            controllerName,
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

    private static string RenderController(
        string controllerName,
        string controllerBaseTypeName,
        IReadOnlyList<EndpointModel> endpoints)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("namespace UO.Mediator.ApiExplorer.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    [global::Microsoft.AspNetCore.Mvc.ApiControllerAttribute]");
        builder.Append("    [global::Microsoft.AspNetCore.Http.TagsAttribute(\"")
            .Append(EscapeCSharpString(endpoints[0].Tag))
            .AppendLine("\")]");
        builder.Append("    public partial class ")
            .Append(controllerName)
            .Append(" : ")
            .AppendLine(controllerBaseTypeName);
        builder.AppendLine("    {");
        builder.AppendLine(
            "        private readonly global::UO.Mediator.Dispatching.IRequestDispatcher _dispatcher;");
        builder.AppendLine();
        builder.Append("        public ")
            .Append(controllerName)
            .AppendLine("(");
        builder.AppendLine(
            "            global::UO.Mediator.Dispatching.IRequestDispatcher dispatcher)");
        builder.AppendLine("        {");
        builder.AppendLine("            _dispatcher = dispatcher;");
        builder.AppendLine("        }");

        foreach (var endpoint in endpoints)
        {
            RenderAction(builder, endpoint);
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void RenderAction(
        StringBuilder builder,
        EndpointModel endpoint)
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

        builder.AppendLine();
        if (endpoint.AllowAnonymous)
        {
            builder.AppendLine(
                "        [global::Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute]");
        }
        else if (endpoint.AuthorizationPolicy is not null)
        {
            builder.Append("        [global::Microsoft.AspNetCore.Authorization.AuthorizeAttribute(Policy = \"")
                .Append(EscapeCSharpString(endpoint.AuthorizationPolicy))
                .AppendLine("\")]");
        }

        foreach (var attribute in endpoint.ActionAttributes)
        {
            builder.Append("        ")
                .AppendLine(attribute);
        }

        builder.Append("        [global::Microsoft.AspNetCore.Mvc.")
            .Append(httpAttribute)
            .Append("(\"")
            .Append(EscapeCSharpString(endpoint.Route))
            .AppendLine("\")]");

        if (endpoint.IsUnit)
        {
            builder.Append(
                    "        public async global::System.Threading.Tasks.Task<global::Microsoft.AspNetCore.Mvc.IActionResult> ")
                .Append(endpoint.ActionName)
                .AppendLine("(");
        }
        else
        {
            builder.Append(
                    "        public async global::System.Threading.Tasks.Task<global::Microsoft.AspNetCore.Mvc.ActionResult<")
                .Append(endpoint.ResponseTypeName)
                .Append(">> ")
                .Append(endpoint.ActionName)
                .AppendLine("(");
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
    }

    private static string EscapeCSharpString(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '\"': builder.Append("\\\""); break;
                case '\0': builder.Append("\\0"); break;
                case '\a': builder.Append("\\a"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\v': builder.Append("\\v"); break;
                default:
                    if (char.IsControl(character))
                    {
                        builder.Append("\\u")
                            .Append(((int)character).ToString(
                                "X4",
                                CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryValidateControllerFragment(
        string? value,
        bool allowEmpty,
        out string error)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (allowEmpty)
            {
                error = string.Empty;
                return true;
            }

            error = "cannot be empty";
            return false;
        }

        if (value.Any(character =>
                !char.IsLetterOrDigit(character) && character != '_'))
        {
            error = $"'{value}' must contain only letters, digits or '_'";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (value.Length == 0 ||
            (!char.IsLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        return value.Skip(1).All(character =>
            char.IsLetterOrDigit(character) || character == '_');
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
            string actionName,
            string tag,
            string? authorizationPolicy,
            bool allowAnonymous,
            IReadOnlyList<string> actionAttributes,
            Location location)
        {
            RequestType = requestType;
            RequestDisplayName = requestDisplayName;
            ResponseTypeName = responseTypeName;
            IsUnit = isUnit;
            HttpMethod = httpMethod;
            Route = route;
            ControllerName = controllerName;
            ActionName = actionName;
            Tag = tag;
            AuthorizationPolicy = authorizationPolicy;
            AllowAnonymous = allowAnonymous;
            ActionAttributes = actionAttributes;
            Location = location;
        }

        public INamedTypeSymbol RequestType { get; }

        public string RequestDisplayName { get; }

        public string ResponseTypeName { get; }

        public bool IsUnit { get; }

        public string HttpMethod { get; }

        public string Route { get; }

        public string ControllerName { get; }

        public string ActionName { get; }

        public string Tag { get; }

        public string? AuthorizationPolicy { get; }

        public bool AllowAnonymous { get; }

        public IReadOnlyList<string> ActionAttributes { get; }

        public Location Location { get; }
    }

    private sealed class EndpointSettings
    {
        public EndpointSettings(
            string? controllerName,
            string? route,
            int httpMethodValue,
            string? authorizationPolicy,
            bool allowAnonymous)
        {
            ControllerName = controllerName;
            Route = route;
            HttpMethodValue = httpMethodValue;
            AuthorizationPolicy = authorizationPolicy;
            AllowAnonymous = allowAnonymous;
        }

        public string? ControllerName { get; }

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

    private readonly struct GeneratorOptions : IEquatable<GeneratorOptions>
    {
        public GeneratorOptions(
            string rootPath,
            string controllerPrefix,
            string controllerSuffix,
            string controllerBase)
        {
            RootPath = rootPath;
            ControllerPrefix = controllerPrefix;
            ControllerSuffix = controllerSuffix;
            ControllerBase = controllerBase;
        }

        public string RootPath { get; }

        public string ControllerPrefix { get; }

        public string ControllerSuffix { get; }

        public string ControllerBase { get; }

        public bool Equals(GeneratorOptions other)
        {
            return string.Equals(RootPath, other.RootPath, StringComparison.Ordinal) &&
                   string.Equals(
                       ControllerPrefix,
                       other.ControllerPrefix,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       ControllerSuffix,
                       other.ControllerSuffix,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       ControllerBase,
                       other.ControllerBase,
                       StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is GeneratorOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = RootPath.GetHashCode();
                hashCode = (hashCode * 397) ^ ControllerPrefix.GetHashCode();
                hashCode = (hashCode * 397) ^ ControllerSuffix.GetHashCode();
                hashCode = (hashCode * 397) ^ ControllerBase.GetHashCode();
                return hashCode;
            }
        }
    }
}

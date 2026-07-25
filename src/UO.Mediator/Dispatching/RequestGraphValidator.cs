using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UO.Mediator.Dispatching;

/// <summary>
/// Validates that every concrete request has exactly one handler and that the DI container resolves exactly one handler.
/// </summary>
public class RequestGraphValidator(
    IOptions<RequestDispatcherOptions> options,
    IServiceProvider serviceProvider)
{
    private readonly RequestDispatcherOptions _options = options.Value;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <summary>
    /// Performs static and DI-resolution validation across all discovered application types.
    /// Throws <see cref="RequestGraphValidationException"/> when the graph is invalid.
    /// </summary>
    public void Validate()
    {
        var applicationTypes = _options.Assemblies
            .Distinct()
            .SelectMany(GetLoadableTypes)
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .ToArray();
        var errors = FindErrors(applicationTypes);

        using var scope = _serviceProvider.CreateScope();
        foreach (var request in DescribeRequests(applicationTypes))
        {
            var handlerContract = request.HasResponse
                ? typeof(IRequestHandler<,>).MakeGenericType(request.RequestType, request.ResponseType)
                : typeof(IRequestHandler<>).MakeGenericType(request.RequestType);
            try
            {
                var resolvedHandlerTypes = scope.ServiceProvider
                    .GetServices(handlerContract)
                    .Select(x => x!.GetType())
                    .Distinct()
                    .ToArray();

                if (resolvedHandlerTypes.Length != 1)
                {
                    errors.Add(
                        $"{request.RequestType.FullName}: DI resolved {resolvedHandlerTypes.Length} handlers for " +
                        $"{handlerContract.FullName}.");
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    $"{request.RequestType.FullName}: DI failed to resolve {handlerContract.FullName}. " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestGraphValidationException(
                "Invalid request handler graph:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(x => $"- {x}")));
        }
    }

    /// <summary>
    /// Statically checks that every request type has exactly one handler.
    /// Does not require a built service provider.
    /// </summary>
    public static List<string> FindErrors(IEnumerable<Type> applicationTypes)
    {
        var types = applicationTypes.ToArray();
        var handlers = types
            .SelectMany(type => type.GetInterfaces()
                .Where(interfaceType => IsHandlerContract(interfaceType) || IsNoResponseHandlerContract(interfaceType))
                .Select(contract => new
                {
                    HandlerType = type,
                    RequestType = contract.GenericTypeArguments[0],
                    ResponseType = IsNoResponseHandlerContract(contract)
                        ? typeof(Unit)
                        : contract.GenericTypeArguments[1],
                    HasResponse = !IsNoResponseHandlerContract(contract)
                }))
            .ToArray();
        var errors = new List<string>();

        foreach (var request in DescribeRequests(types))
        {
            var matches = handlers
                .Where(x =>
                    x.RequestType == request.RequestType &&
                    x.ResponseType == request.ResponseType &&
                    x.HasResponse == request.HasResponse)
                .Select(x => x.HandlerType)
                .Distinct()
                .ToArray();

            if (matches.Length != 1)
            {
                errors.Add(
                    $"{request.RequestType.FullName}: expected exactly one handler for response " +
                    $"{request.ResponseType.FullName}, found {matches.Length}.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<RequestDescriptor> DescribeRequests(IEnumerable<Type> types)
    {
        return types
            .SelectMany(type =>
            {
                if (typeof(IRequest).IsAssignableFrom(type))
                {
                    return [new RequestDescriptor(type, typeof(Unit), false)];
                }

                return type.GetInterfaces()
                    .Where(IsRequestContract)
                    .Select(contract => new RequestDescriptor(
                        type,
                        contract.GenericTypeArguments[0],
                        true));
            })
            .Distinct()
            .ToArray();
    }

    private static bool IsRequestContract(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequest<>);
    }

    private static bool IsHandlerContract(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>);
    }

    private static bool IsNoResponseHandlerContract(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequestHandler<>);
    }

    private static IEnumerable<Type> GetLoadableTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private sealed record RequestDescriptor(Type RequestType, Type ResponseType, bool HasResponse);
}

public sealed class RequestGraphValidationException(string message) : InvalidOperationException(message);

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace UO.Mediator;

/// <summary>
/// Tracks registrations emitted by a generated assembly in constant time while
/// preserving handler and behavior registrations supplied by the application.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedServiceRegistrationState
{
    private readonly HashSet<(Type ServiceType, Type ImplementationType)> _registrations = [];
    private readonly HashSet<Type> _factoryServiceTypes = [];

    internal GeneratedServiceRegistrationState(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService)
            {
                continue;
            }

            var implementationType = descriptor.ImplementationType ??
                descriptor.ImplementationInstance?.GetType();
            if (implementationType is null)
            {
                _factoryServiceTypes.Add(descriptor.ServiceType);
                continue;
            }

            _registrations.Add((descriptor.ServiceType, implementationType));
        }
    }

    internal void TryAddTransient(
        IServiceCollection services,
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type implementationType)
    {
        if (_factoryServiceTypes.Contains(serviceType) ||
            !_registrations.Add((serviceType, implementationType)))
        {
            return;
        }

        services.Add(ServiceDescriptor.Transient(serviceType, implementationType));
    }

    internal void TryAddSingleton(
        IServiceCollection services,
        Type serviceType,
        object implementation)
    {
        if (_factoryServiceTypes.Contains(serviceType) ||
            !_registrations.Add((serviceType, implementation.GetType())))
        {
            return;
        }

        services.Add(ServiceDescriptor.Singleton(serviceType, implementation));
    }
}

namespace UO.Mediator.ApiExplorer;

/// <summary>
/// Exposes a mediator request as an ASP.NET Core API endpoint when the consuming API host
/// references the UO.Mediator.ApiExplorer source generator.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MediatorApiExplorerAttribute : Attribute
{
    /// <summary>
    /// Groups the request with other requests in the same generated controller.
    /// When omitted, the controller name is derived from the request name convention.
    /// </summary>
    public string? ControllerName { get; set; }

    /// <summary>
    /// Overrides the convention-based absolute route.
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    /// Overrides the convention-based HTTP method.
    /// </summary>
    public MediatorHttpMethod HttpMethod { get; set; } = MediatorHttpMethod.Convention;

    /// <summary>
    /// Adds an ASP.NET Core authorization policy to the generated controller.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Marks the generated controller as allowing anonymous access.
    /// </summary>
    public bool AllowAnonymous { get; set; }
}

/// <summary>
/// HTTP methods supported by <see cref="MediatorApiExplorerAttribute"/>.
/// </summary>
public enum MediatorHttpMethod
{
    /// <summary>
    /// Derives the HTTP method from the request type name.
    /// </summary>
    Convention = 0,

    /// <summary>
    /// HTTP GET.
    /// </summary>
    Get = 1,

    /// <summary>
    /// HTTP POST.
    /// </summary>
    Post = 2,

    /// <summary>
    /// HTTP PUT.
    /// </summary>
    Put = 3,

    /// <summary>
    /// HTTP DELETE.
    /// </summary>
    Delete = 4,

    /// <summary>
    /// HTTP PATCH.
    /// </summary>
    Patch = 5
}

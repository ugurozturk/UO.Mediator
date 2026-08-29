namespace UO.Mediator.Demo.Books;

/// <summary>
/// Example custom endpoint metadata copied from a mediator request to the
/// generated controller action by UO.Mediator.ApiExplorer.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = false)]
public sealed class DemoActionMetadataAttribute(string operation) : Attribute
{
    public string Operation { get; } = operation;

    public string? Description { get; set; }
}

namespace UO.Mediator.Dispatching;

/// <summary>
/// Options for the optional request logging behavior.
/// </summary>
public sealed class RequestLoggingOptions
{
    /// <summary>
    /// Requests that take longer than this threshold are logged as warnings.
    /// Defaults to 1 second.
    /// </summary>
    public TimeSpan SlowRequestThreshold { get; set; } = TimeSpan.FromSeconds(1);
}

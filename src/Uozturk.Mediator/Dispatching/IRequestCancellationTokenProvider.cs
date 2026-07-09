namespace Uozturk.Mediator.Dispatching;

/// <summary>
/// Framework-neutral hook for resolving and flowing cancellation tokens during dispatch.
/// </summary>
public interface IRequestCancellationTokenProvider
{
    /// <summary>
    /// Returns the effective cancellation token for the current dispatch.
    /// </summary>
    CancellationToken GetCancellationToken(CancellationToken cancellationToken = default);

    /// <summary>
    /// Establishes the effective token for ambient consumers during the dispatch.
    /// </summary>
    IDisposable Use(CancellationToken cancellationToken);
}


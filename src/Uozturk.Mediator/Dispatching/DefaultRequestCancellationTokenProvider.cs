namespace Uozturk.Mediator.Dispatching;

internal sealed class DefaultRequestCancellationTokenProvider : IRequestCancellationTokenProvider
{
    public CancellationToken GetCancellationToken(CancellationToken cancellationToken = default)
    {
        return cancellationToken;
    }

    public IDisposable Use(CancellationToken cancellationToken)
    {
        return NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static NullDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}


using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UO.Mediator.Dispatching;

/// <summary>
/// Built-in behaviour that logs request name and duration. Logs as warning when the configured slow threshold is exceeded.
/// </summary>
public class RequestLoggingBehavior<TRequest, TResponse>(
    ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger,
    IOptions<RequestDispatcherOptions> options) : IRequestBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<RequestLoggingBehavior<TRequest, TResponse>> _logger = logger;
    private readonly RequestDispatcherOptions _options = options.Value;

    /// <inheritdoc />
    public int Order => int.MinValue;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("Dispatching request {RequestName}", requestName);
        try
        {
            return await next();
        }
        finally
        {
            stopwatch.Stop();
            if (stopwatch.Elapsed >= _options.SlowRequestThreshold)
            {
                _logger.LogWarning(
                    "Slow request {RequestName} completed in {ElapsedMilliseconds} ms",
                    requestName,
                    stopwatch.Elapsed.TotalMilliseconds);
            }
            else
            {
                _logger.LogDebug(
                    "Request {RequestName} completed in {ElapsedMilliseconds} ms",
                    requestName,
                    stopwatch.Elapsed.TotalMilliseconds);
            }
        }
    }
}

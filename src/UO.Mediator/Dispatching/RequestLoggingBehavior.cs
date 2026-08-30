using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UO.Mediator.Dispatching;

/// <summary>
/// Built-in behaviour that logs request name and duration. Logs as warning when the configured slow threshold is exceeded.
/// </summary>
public partial class RequestLoggingBehavior<TRequest, TResponse>(
    ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger,
    IOptions<RequestLoggingOptions> options) : IRequestBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<RequestLoggingBehavior<TRequest, TResponse>> _logger = logger;
    private readonly RequestLoggingOptions _options = options.Value;

    /// <inheritdoc />
    public int Order => int.MinValue;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerNext<TRequest, TResponse> next)
    {
        var requestName = typeof(TRequest).Name;
        var startTimestamp = Stopwatch.GetTimestamp();

        LogDispatching(_logger, requestName);
        try
        {
            return await next.InvokeAsync();
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            if (elapsed >= _options.SlowRequestThreshold)
            {
                LogSlowRequestCompleted(_logger, requestName, elapsed.TotalMilliseconds);
            }
            else
            {
                LogRequestCompleted(_logger, requestName, elapsed.TotalMilliseconds);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Dispatching request {RequestName}")]
    private static partial void LogDispatching(ILogger logger, string requestName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Request {RequestName} completed in {ElapsedMilliseconds} ms")]
    private static partial void LogRequestCompleted(
        ILogger logger,
        string requestName,
        double elapsedMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Slow request {RequestName} completed in {ElapsedMilliseconds} ms")]
    private static partial void LogSlowRequestCompleted(
        ILogger logger,
        string requestName,
        double elapsedMilliseconds);
}

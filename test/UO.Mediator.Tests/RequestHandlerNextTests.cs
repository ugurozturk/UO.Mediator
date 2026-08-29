using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Dispatching;
using Xunit;

namespace UO.Mediator.Tests;

public class RequestHandlerNextTests
{
    [Fact]
    public async Task Should_Run_One_Response_Behavior_And_Handler_Exactly_Once()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddTransient<IRequestHandler<TracedResponseRequest, int>, TracedResponseHandler>();
            services.AddTransient<IRequestBehavior<TracedResponseRequest, int>, EarlierTracedBehavior>();
        });
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var request = new TracedResponseRequest(42);

        var response = await dispatcher.DispatchAsync(request);

        Assert.Equal(42, response);
        Assert.Equal(1, request.HandlerCount);
        Assert.Equal(["earlier-before", "handler", "earlier-after"], request.Trace);
    }

    [Fact]
    public async Task Should_Preserve_Response_And_Run_Multiple_Behaviors_Around_Handler()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddTransient<IRequestHandler<TracedResponseRequest, int>, TracedResponseHandler>();
            services.AddTransient<IRequestBehavior<TracedResponseRequest, int>, LaterTracedBehavior>();
            services.AddTransient<IRequestBehavior<TracedResponseRequest, int>, EarlierTracedBehavior>();
        });
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var request = new TracedResponseRequest(42);

        var response = await dispatcher.DispatchAsync(request);

        Assert.Equal(42, response);
        Assert.Equal(1, request.HandlerCount);
        Assert.Equal(
            ["earlier-before", "later-before", "handler", "later-after", "earlier-after"],
            request.Trace);
    }

    [Fact]
    public async Task Should_Invoke_Same_Downstream_Position_Twice()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddTransient<IRequestHandler<RepeatedNextRequest, int>, RepeatedNextHandler>();
            services.AddTransient<IRequestBehavior<RepeatedNextRequest, int>, RepeatingBehavior>();
            services.AddTransient<IRequestBehavior<RepeatedNextRequest, int>, RepeatedDownstreamBehavior>();
        });
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var request = new RepeatedNextRequest();

        var response = await dispatcher.DispatchAsync(request);

        Assert.Equal(2, response);
        Assert.Equal(2, request.DownstreamCount);
        Assert.Equal(2, request.HandlerCount);
    }

    [Fact]
    public async Task Should_Invoke_Same_Downstream_Position_Concurrently()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddTransient<IRequestHandler<ConcurrentNextRequest, int>, ConcurrentNextHandler>();
            services.AddTransient<IRequestBehavior<ConcurrentNextRequest, int>, ConcurrentBehavior>();
            services.AddTransient<IRequestBehavior<ConcurrentNextRequest, int>, ConcurrentDownstreamBehavior>();
        });
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var request = new ConcurrentNextRequest();

        var response = await dispatcher.DispatchAsync(request);

        Assert.Equal(1, response);
        Assert.Equal(2, request.DownstreamCount);
        Assert.Equal(2, request.HandlerCount);
    }

    [Fact]
    public async Task Should_Short_Circuit_Without_Invoking_Downstream_Pipeline()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddTransient<IRequestHandler<ShortCircuitRequest, int>, ShortCircuitHandler>();
            services.AddTransient<IRequestBehavior<ShortCircuitRequest, int>, ShortCircuitBehavior>();
            services.AddTransient<IRequestBehavior<ShortCircuitRequest, int>, ShortCircuitDownstreamBehavior>();
        });
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var request = new ShortCircuitRequest();

        var response = await dispatcher.DispatchAsync(request);

        Assert.Equal(99, response);
        Assert.Equal(0, request.DownstreamCount);
        Assert.Equal(0, request.HandlerCount);
    }

    [Fact]
    public async Task Should_Propagate_The_Same_Downstream_Behavior_Exception()
    {
        var expected = new InvalidOperationException("Expected behavior failure.");
        using var provider = BuildProvider(services =>
        {
            services.AddTransient<IRequestHandler<BehaviorFailureRequest, int>, BehaviorFailureHandler>();
            services.AddTransient<IRequestBehavior<BehaviorFailureRequest, int>, PassThroughFailureBehavior>();
            services.AddTransient<IRequestBehavior<BehaviorFailureRequest, int>, ThrowingFailureBehavior>();
        });
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new BehaviorFailureRequest(expected)));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Logging_Behavior_Should_Propagate_The_Same_Handler_Exception()
    {
        var expected = new InvalidOperationException("Expected logged handler failure.");
        using var provider = BuildProvider(
            services =>
            {
                services.AddTransient<
                    IRequestHandler<LoggedHandlerFailureRequest, int>,
                    LoggedHandlerFailureHandler>();
            },
            includeDefaultLogging: true);
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new LoggedHandlerFailureRequest(expected)));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Should_Preserve_Transient_Handler_And_Behavior_Lifetimes()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddTransient<
                IRequestHandler<TransientPipelineRequest, int>,
                TransientPipelineHandler>();
            services.AddTransient<
                IRequestBehavior<TransientPipelineRequest, int>,
                TransientPipelineBehavior>();
        });

        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var first = new TransientPipelineRequest();
        var second = new TransientPipelineRequest();

        await dispatcher.DispatchAsync(first);
        await dispatcher.DispatchAsync(second);

        Assert.NotEqual(first.HandlerInstance, second.HandlerInstance);
        Assert.NotEqual(first.BehaviorInstance, second.BehaviorInstance);
    }

    [Fact]
    public async Task Should_Preserve_Scoped_Handler_And_Behavior_Lifetimes()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddScoped<IRequestHandler<ScopedPipelineRequest, int>, ScopedPipelineHandler>();
            services.AddScoped<IRequestBehavior<ScopedPipelineRequest, int>, ScopedPipelineBehavior>();
        });
        var first = new ScopedPipelineRequest();
        var second = new ScopedPipelineRequest();
        var third = new ScopedPipelineRequest();

        using (var scope = provider.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.DispatchAsync(first);
            await dispatcher.DispatchAsync(second);
        }

        using (var scope = provider.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.DispatchAsync(third);
        }

        Assert.Equal(first.HandlerInstance, second.HandlerInstance);
        Assert.Equal(first.BehaviorInstance, second.BehaviorInstance);
        Assert.NotEqual(first.HandlerInstance, third.HandlerInstance);
        Assert.NotEqual(first.BehaviorInstance, third.BehaviorInstance);
    }

    [Fact]
    public void Behavior_Request_Type_Variance_Change_Should_Be_Intentional()
    {
        var requestParameter = typeof(IRequestBehavior<,>).GetGenericArguments()[0];
        var nextType = typeof(RequestHandlerNext<TracedResponseRequest, int>);

        Assert.Equal(GenericParameterAttributes.None, requestParameter.GenericParameterAttributes);
        Assert.True(nextType.IsValueType);
        Assert.True(nextType.IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
        Assert.Empty(nextType.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(typeof(IRequestBehavior<,>).Assembly.GetType(
            "UO.Mediator.Dispatching.RequestHandlerDelegate`1"));
    }

    private static ServiceProvider BuildProvider(
        Action<IServiceCollection> configure,
        bool includeDefaultLogging = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUOMediator();
        configure(services);

        if (!includeDefaultLogging)
        {
            var loggingBehavior = services.Single(descriptor =>
                descriptor.ServiceType == typeof(IRequestBehavior<,>) &&
                descriptor.ImplementationType == typeof(RequestLoggingBehavior<,>));
            services.Remove(loggingBehavior);
        }

        return services.BuildServiceProvider(validateScopes: true);
    }
}

public sealed class TracedResponseRequest(int value) : IRequest<int>
{
    public int Value { get; } = value;
    public int HandlerCount { get; set; }
    public List<string> Trace { get; } = [];
}

public sealed class TracedResponseHandler : IRequestHandler<TracedResponseRequest, int>
{
    public Task<int> HandleAsync(TracedResponseRequest request)
    {
        request.HandlerCount++;
        request.Trace.Add("handler");
        return Task.FromResult(request.Value);
    }
}

public sealed class EarlierTracedBehavior : IRequestBehavior<TracedResponseRequest, int>
{
    public int Order => 10;

    public async Task<int> HandleAsync(
        TracedResponseRequest request,
        RequestHandlerNext<TracedResponseRequest, int> next)
    {
        request.Trace.Add("earlier-before");
        var response = await next.InvokeAsync();
        request.Trace.Add("earlier-after");
        return response;
    }
}

public sealed class LaterTracedBehavior : IRequestBehavior<TracedResponseRequest, int>
{
    public int Order => 20;

    public async Task<int> HandleAsync(
        TracedResponseRequest request,
        RequestHandlerNext<TracedResponseRequest, int> next)
    {
        request.Trace.Add("later-before");
        var response = await next.InvokeAsync();
        request.Trace.Add("later-after");
        return response;
    }
}

public sealed class RepeatedNextRequest : IRequest<int>
{
    public int DownstreamCount;
    public int HandlerCount;
}

public sealed class RepeatedNextHandler : IRequestHandler<RepeatedNextRequest, int>
{
    public Task<int> HandleAsync(RepeatedNextRequest request)
    {
        return Task.FromResult(Interlocked.Increment(ref request.HandlerCount));
    }
}

public sealed class RepeatingBehavior : IRequestBehavior<RepeatedNextRequest, int>
{
    public int Order => 0;

    public async Task<int> HandleAsync(
        RepeatedNextRequest request,
        RequestHandlerNext<RepeatedNextRequest, int> next)
    {
        await next.InvokeAsync();
        return await next.InvokeAsync();
    }
}

public sealed class RepeatedDownstreamBehavior : IRequestBehavior<RepeatedNextRequest, int>
{
    public int Order => 1;

    public Task<int> HandleAsync(
        RepeatedNextRequest request,
        RequestHandlerNext<RepeatedNextRequest, int> next)
    {
        Interlocked.Increment(ref request.DownstreamCount);
        return next.InvokeAsync();
    }
}

public sealed class ConcurrentNextRequest : IRequest<int>
{
    public int DownstreamCount;
    public int HandlerCount;
}

public sealed class ConcurrentNextHandler : IRequestHandler<ConcurrentNextRequest, int>
{
    public async Task<int> HandleAsync(ConcurrentNextRequest request)
    {
        await Task.Yield();
        Interlocked.Increment(ref request.HandlerCount);
        return 1;
    }
}

public sealed class ConcurrentBehavior : IRequestBehavior<ConcurrentNextRequest, int>
{
    public int Order => 0;

    public async Task<int> HandleAsync(
        ConcurrentNextRequest request,
        RequestHandlerNext<ConcurrentNextRequest, int> next)
    {
        var responses = await Task.WhenAll(next.InvokeAsync(), next.InvokeAsync());
        return responses[0];
    }
}

public sealed class ConcurrentDownstreamBehavior : IRequestBehavior<ConcurrentNextRequest, int>
{
    public int Order => 1;

    public async Task<int> HandleAsync(
        ConcurrentNextRequest request,
        RequestHandlerNext<ConcurrentNextRequest, int> next)
    {
        await Task.Yield();
        Interlocked.Increment(ref request.DownstreamCount);
        return await next.InvokeAsync();
    }
}

public sealed class ShortCircuitRequest : IRequest<int>
{
    public int DownstreamCount;
    public int HandlerCount;
}

public sealed class ShortCircuitHandler : IRequestHandler<ShortCircuitRequest, int>
{
    public Task<int> HandleAsync(ShortCircuitRequest request)
    {
        request.HandlerCount++;
        return Task.FromResult(1);
    }
}

public sealed class ShortCircuitBehavior : IRequestBehavior<ShortCircuitRequest, int>
{
    public int Order => 0;

    public Task<int> HandleAsync(
        ShortCircuitRequest request,
        RequestHandlerNext<ShortCircuitRequest, int> next)
    {
        return Task.FromResult(99);
    }
}

public sealed class ShortCircuitDownstreamBehavior : IRequestBehavior<ShortCircuitRequest, int>
{
    public int Order => 1;

    public Task<int> HandleAsync(
        ShortCircuitRequest request,
        RequestHandlerNext<ShortCircuitRequest, int> next)
    {
        request.DownstreamCount++;
        return next.InvokeAsync();
    }
}

public sealed record BehaviorFailureRequest(InvalidOperationException Exception) : IRequest<int>;

public sealed class BehaviorFailureHandler : IRequestHandler<BehaviorFailureRequest, int>
{
    public Task<int> HandleAsync(BehaviorFailureRequest request)
    {
        return Task.FromResult(1);
    }
}

public sealed class PassThroughFailureBehavior : IRequestBehavior<BehaviorFailureRequest, int>
{
    public int Order => 0;

    public Task<int> HandleAsync(
        BehaviorFailureRequest request,
        RequestHandlerNext<BehaviorFailureRequest, int> next)
    {
        return next.InvokeAsync();
    }
}

public sealed class ThrowingFailureBehavior : IRequestBehavior<BehaviorFailureRequest, int>
{
    public int Order => 1;

    public Task<int> HandleAsync(
        BehaviorFailureRequest request,
        RequestHandlerNext<BehaviorFailureRequest, int> next)
    {
        return Task.FromException<int>(request.Exception);
    }
}

public sealed record LoggedHandlerFailureRequest(InvalidOperationException Exception) : IRequest<int>;

public sealed class LoggedHandlerFailureHandler : IRequestHandler<LoggedHandlerFailureRequest, int>
{
    public Task<int> HandleAsync(LoggedHandlerFailureRequest request)
    {
        return Task.FromException<int>(request.Exception);
    }
}

public abstract class PipelineLifetimeRequest : IRequest<int>
{
    public Guid HandlerInstance { get; set; }
    public Guid BehaviorInstance { get; set; }
}

public sealed class TransientPipelineRequest : PipelineLifetimeRequest;

public sealed class ScopedPipelineRequest : PipelineLifetimeRequest;

public sealed class TransientPipelineHandler : IRequestHandler<TransientPipelineRequest, int>
{
    private readonly Guid _instance = Guid.NewGuid();

    public Task<int> HandleAsync(TransientPipelineRequest request)
    {
        request.HandlerInstance = _instance;
        return Task.FromResult(1);
    }
}

public sealed class TransientPipelineBehavior : IRequestBehavior<TransientPipelineRequest, int>
{
    private readonly Guid _instance = Guid.NewGuid();

    public Task<int> HandleAsync(
        TransientPipelineRequest request,
        RequestHandlerNext<TransientPipelineRequest, int> next)
    {
        request.BehaviorInstance = _instance;
        return next.InvokeAsync();
    }
}

public sealed class ScopedPipelineHandler : IRequestHandler<ScopedPipelineRequest, int>
{
    private readonly Guid _instance = Guid.NewGuid();

    public Task<int> HandleAsync(ScopedPipelineRequest request)
    {
        request.HandlerInstance = _instance;
        return Task.FromResult(1);
    }
}

public sealed class ScopedPipelineBehavior : IRequestBehavior<ScopedPipelineRequest, int>
{
    private readonly Guid _instance = Guid.NewGuid();

    public Task<int> HandleAsync(
        ScopedPipelineRequest request,
        RequestHandlerNext<ScopedPipelineRequest, int> next)
    {
        request.BehaviorInstance = _instance;
        return next.InvokeAsync();
    }
}

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UO.Mediator.Dispatching;
using Xunit;

namespace UO.Mediator.Tests;

public class RequestDispatcherTests
{
    [Fact]
    public async Task Should_Dispatch_Response_And_No_Response_Requests()
    {
        using var provider = BuildProvider();
        provider.ValidateUOMediator();
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var command = new RecordCommand();

        var response = await dispatcher.DispatchAsync(new EchoRequest("test"));
        await dispatcher.DispatchAsync(command);

        Assert.Equal("TEST", response);
        Assert.True(command.WasHandled);
    }

    [Fact]
    public async Task Should_Execute_No_Response_Handler_Exactly_Once_Without_Behaviors()
    {
        using var provider = BuildProviderWithServices();
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var command = new CountedCommand();

        await dispatcher.DispatchAsync(command);

        Assert.Equal(1, command.HandleCount);
    }

    [Fact]
    public async Task Should_Run_No_Response_Behavior_When_Registered()
    {
        using var provider = BuildProviderWithServices();
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var trace = new List<string>();

        await dispatcher.DispatchAsync(new SingleBehaviorCommand(trace));

        Assert.Equal(["behavior", "handler"], trace);
    }

    [Fact]
    public async Task Should_Run_Behaviors_In_Order()
    {
        using var provider = BuildProviderWithServices();
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var trace = new List<string>();

        await dispatcher.DispatchAsync(new OrderedRequest(trace));

        Assert.Equal(
            ["first-before", "second-before", "handler", "second-after", "first-after"],
            trace);
    }

    [Fact]
    public async Task Should_Cache_Pipeline_Metadata_But_Resolve_Scoped_Behaviors_Per_Dispatch()
    {
        var probe = new PreparedPipelineProbe();
        using var provider = BuildPreparedPipelineProvider(probe);
        var firstTrace = new List<int>();
        var secondTrace = new List<int>();

        using (var scope = provider.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.DispatchAsync(new PreparedPipelineRequest(firstTrace));
        }

        using (var scope = provider.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.DispatchAsync(new PreparedPipelineRequest(secondTrace));
        }

        Assert.Equal(2, probe.CreatedCount);
        Assert.Equal(1, probe.OrderReadCount);
        Assert.NotEqual(Assert.Single(firstTrace), Assert.Single(secondTrace));

        var otherProbe = new PreparedPipelineProbe();
        using var otherProvider = BuildPreparedPipelineProvider(otherProbe);
        using var otherScope = otherProvider.CreateScope();
        var otherDispatcher = otherScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();

        await otherDispatcher.DispatchAsync(new PreparedPipelineRequest([]));

        Assert.Equal(1, otherProbe.CreatedCount);
        Assert.Equal(1, otherProbe.OrderReadCount);
    }

    [Fact]
    public async Task Should_Propagate_Handler_Exceptions()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new FailingRequest()));

        Assert.Equal("Expected test failure.", exception.Message);
    }

    [Fact]
    public async Task Should_Propagate_The_Same_Handler_Exception_Without_Behaviors()
    {
        using var provider = BuildProviderWithServices();
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var expected = new InvalidOperationException("Expected fast-path failure.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new AsyncFailingCommand(expected)));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Should_Respect_No_Response_Handler_Lifetimes_Without_Behaviors()
    {
        var transientProbe = new TransientHandlerProbe();
        var scopedProbe = new ScopedHandlerProbe();
        using var provider = BuildProviderWithServices(services =>
        {
            services.AddSingleton(transientProbe);
            services.AddSingleton(scopedProbe);
            services.AddScoped<
                IRequestHandler<ScopedLifetimeCommand>,
                ScopedLifetimeCommandHandler>();
        });

        var firstTransient = new TransientLifetimeCommand();
        var secondTransient = new TransientLifetimeCommand();
        var firstScoped = new ScopedLifetimeCommand();
        var secondScoped = new ScopedLifetimeCommand();
        var thirdScoped = new ScopedLifetimeCommand();

        using (var scope = provider.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.DispatchAsync(firstTransient);
            await dispatcher.DispatchAsync(secondTransient);
            await dispatcher.DispatchAsync(firstScoped);
            await dispatcher.DispatchAsync(secondScoped);
        }

        using (var scope = provider.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.DispatchAsync(thirdScoped);
        }

        Assert.Equal(2, transientProbe.CreatedCount);
        Assert.NotEqual(firstTransient.HandlerInstance, secondTransient.HandlerInstance);
        Assert.Equal(2, scopedProbe.CreatedCount);
        Assert.Equal(firstScoped.HandlerInstance, secondScoped.HandlerInstance);
        Assert.NotEqual(firstScoped.HandlerInstance, thirdScoped.HandlerInstance);
    }

    [Fact]
    public async Task Cancellation_Should_Be_Opt_In_Through_Abp_Provider()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var provider = BuildProvider(cancellation.Token);
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var ignoredRequest = new CancellationIgnoredRequest();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(new CancellationAwareRequest()));
        await dispatcher.DispatchAsync(ignoredRequest);

        Assert.True(ignoredRequest.WasHandled);
    }

    [Fact]
    public void Mediator_Registration_Should_Not_Register_A_Cancellation_Token_Provider()
    {
        var services = new ServiceCollection();

        services.AddUOMediator(typeof(RequestDispatcherTests).Assembly);

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(ITestCancellationTokenProvider));
    }

    [Fact]
    public void Mediator_Registration_Should_Not_Enable_Request_Logging_By_Default()
    {
        var services = new ServiceCollection();

        services.AddUOMediator(typeof(RequestDispatcherTests).Assembly);

        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IRequestBehavior<,>) &&
                descriptor.ImplementationType == typeof(RequestLoggingBehavior<,>));
    }

    [Fact]
    public void Request_Logging_Should_Be_Explicit_Configurable_And_Idempotent()
    {
        var services = new ServiceCollection();
        var threshold = TimeSpan.FromMilliseconds(750);
        services.AddLogging();
        services.AddUOMediator(typeof(RequestDispatcherTests).Assembly);

        services.AddUOMediatorRequestLogging(options =>
            options.SlowRequestThreshold = threshold);
        services.AddUOMediatorRequestLogging();

        var loggingBehavior = Assert.Single(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IRequestBehavior<,>) &&
                descriptor.ImplementationType == typeof(RequestLoggingBehavior<,>));
        Assert.Equal(ServiceLifetime.Transient, loggingBehavior.Lifetime);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var options = provider.GetRequiredService<IOptions<RequestLoggingOptions>>().Value;
        Assert.Equal(threshold, options.SlowRequestThreshold);
    }

    [Fact]
    public void Public_Mediator_Contracts_Should_Not_Expose_CancellationToken()
    {
        var contracts = new[]
        {
            typeof(IRequestHandler<EchoRequest, string>),
            typeof(IRequestHandler<RecordCommand>),
            typeof(IRequestBehavior<OrderedRequest, Unit>),
            typeof(IRequestDispatcher)
        };

        var parameters = contracts
            .SelectMany(contract => contract.GetMethods())
            .SelectMany(method => method.GetParameters())
            .ToArray();

        Assert.DoesNotContain(
            parameters,
            parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    [Fact]
    public void Validator_Should_Report_Missing_And_Duplicate_Handlers()
    {
        var missingErrors = RequestGraphValidator.FindErrors([typeof(MissingRequest)]);
        var duplicateErrors = RequestGraphValidator.FindErrors([
            typeof(DuplicateRequest),
            typeof(FirstDuplicateHandler),
            typeof(SecondDuplicateHandler)
        ]);

        Assert.Contains("found 0", Assert.Single(missingErrors), StringComparison.Ordinal);
        Assert.Contains("found 2", Assert.Single(duplicateErrors), StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITestCancellationTokenProvider>(
            new TestCancellationTokenProvider(cancellationToken));
        services.AddSingleton<TransientHandlerProbe>();
        services.AddSingleton<ScopedHandlerProbe>();
        services.AddUOMediator(typeof(RequestDispatcherTests).Assembly);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider BuildProviderWithServices(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddUOMediator(typeof(RequestDispatcherTests).Assembly);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider BuildPreparedPipelineProvider(PreparedPipelineProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(probe);
        services.AddScoped<
            IRequestBehavior<PreparedPipelineRequest, string>,
            PreparedPipelineBehavior>();
        services.AddUOMediator(typeof(RequestDispatcherTests).Assembly);
        return services.BuildServiceProvider(validateScopes: true);
    }
}

public sealed record EchoRequest(string Value) : IRequest<string>;

public sealed class EchoRequestHandler : IRequestHandler<EchoRequest, string>
{
    public Task<string> HandleAsync(EchoRequest request)
    {
        return Task.FromResult(request.Value.ToUpperInvariant());
    }
}

public sealed record RecordCommand : IRequest
{
    public bool WasHandled { get; set; }
}

public sealed class RecordCommandHandler : IRequestHandler<RecordCommand>
{
    public Task HandleAsync(RecordCommand request)
    {
        request.WasHandled = true;
        return Task.CompletedTask;
    }
}

public sealed class CountedCommand : IRequest
{
    public int HandleCount { get; set; }
}

public sealed class CountedCommandHandler : IRequestHandler<CountedCommand>
{
    public Task HandleAsync(CountedCommand request)
    {
        request.HandleCount++;
        return Task.CompletedTask;
    }
}

public sealed record SingleBehaviorCommand(List<string> Trace) : IRequest;

public sealed class SingleBehaviorCommandHandler : IRequestHandler<SingleBehaviorCommand>
{
    public Task HandleAsync(SingleBehaviorCommand request)
    {
        request.Trace.Add("handler");
        return Task.CompletedTask;
    }
}

public sealed class SingleBehaviorCommandBehavior : IRequestBehavior<SingleBehaviorCommand, Unit>
{
    public Task<Unit> HandleAsync(
        SingleBehaviorCommand request,
        RequestHandlerNext<SingleBehaviorCommand, Unit> next)
    {
        request.Trace.Add("behavior");
        return next.InvokeAsync();
    }
}

public sealed record OrderedRequest(List<string> Trace) : IRequest;

public sealed class OrderedRequestHandler : IRequestHandler<OrderedRequest>
{
    public Task HandleAsync(OrderedRequest request)
    {
        request.Trace.Add("handler");
        return Task.CompletedTask;
    }
}

public sealed class FirstOrderedBehavior : IRequestBehavior<OrderedRequest, Unit>
{
    public int Order => 10;

    public async Task<Unit> HandleAsync(
        OrderedRequest request,
        RequestHandlerNext<OrderedRequest, Unit> next)
    {
        request.Trace.Add("first-before");
        var result = await next.InvokeAsync();
        request.Trace.Add("first-after");
        return result;
    }
}

public sealed class SecondOrderedBehavior : IRequestBehavior<OrderedRequest, Unit>
{
    public int Order => 20;

    public async Task<Unit> HandleAsync(
        OrderedRequest request,
        RequestHandlerNext<OrderedRequest, Unit> next)
    {
        request.Trace.Add("second-before");
        var result = await next.InvokeAsync();
        request.Trace.Add("second-after");
        return result;
    }
}

public sealed record PreparedPipelineRequest(List<int> BehaviorInstances) : IRequest<string>;

public sealed class PreparedPipelineRequestHandler : IRequestHandler<PreparedPipelineRequest, string>
{
    public Task<string> HandleAsync(PreparedPipelineRequest request)
    {
        return Task.FromResult("handled");
    }
}

public sealed class PreparedPipelineBehavior(PreparedPipelineProbe probe)
    : IRequestBehavior<PreparedPipelineRequest, string>
{
    private readonly int _instanceId = probe.RecordInstanceCreated();

    public int Order => probe.RecordOrderRead();

    public Task<string> HandleAsync(
        PreparedPipelineRequest request,
        RequestHandlerNext<PreparedPipelineRequest, string> next)
    {
        request.BehaviorInstances.Add(_instanceId);
        return next.InvokeAsync();
    }
}

public sealed class PreparedPipelineProbe
{
    private int _createdCount;
    private int _orderReadCount;

    public int CreatedCount => _createdCount;

    public int OrderReadCount => _orderReadCount;

    public int RecordInstanceCreated()
    {
        return Interlocked.Increment(ref _createdCount);
    }

    public int RecordOrderRead()
    {
        Interlocked.Increment(ref _orderReadCount);
        return 0;
    }
}

public sealed record CancellationAwareRequest : IRequest;

public sealed class CancellationAwareRequestHandler(
    ITestCancellationTokenProvider cancellationTokenProvider)
    : IRequestHandler<CancellationAwareRequest>
{
    public Task HandleAsync(CancellationAwareRequest request)
    {
        cancellationTokenProvider.Token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class CancellationIgnoredRequest : IRequest
{
    public bool WasHandled { get; set; }
}

public sealed class CancellationIgnoredRequestHandler : IRequestHandler<CancellationIgnoredRequest>
{
    public Task HandleAsync(CancellationIgnoredRequest request)
    {
        request.WasHandled = true;
        return Task.CompletedTask;
    }
}

public sealed record FailingRequest : IRequest;

public sealed class FailingRequestHandler : IRequestHandler<FailingRequest>
{
    public Task HandleAsync(FailingRequest request)
    {
        throw new InvalidOperationException("Expected test failure.");
    }
}

public sealed record AsyncFailingCommand(Exception Exception) : IRequest;

public sealed class AsyncFailingCommandHandler : IRequestHandler<AsyncFailingCommand>
{
    public Task HandleAsync(AsyncFailingCommand request)
    {
        return Task.FromException(request.Exception);
    }
}

public sealed class TransientLifetimeCommand : IRequest
{
    public int HandlerInstance { get; set; }
}

public sealed class TransientLifetimeCommandHandler(TransientHandlerProbe probe)
    : IRequestHandler<TransientLifetimeCommand>
{
    private readonly int _instanceId = probe.RecordInstanceCreated();

    public Task HandleAsync(TransientLifetimeCommand request)
    {
        request.HandlerInstance = _instanceId;
        return Task.CompletedTask;
    }
}

public sealed class ScopedLifetimeCommand : IRequest
{
    public int HandlerInstance { get; set; }
}

public sealed class ScopedLifetimeCommandHandler(ScopedHandlerProbe probe)
    : IRequestHandler<ScopedLifetimeCommand>
{
    private readonly int _instanceId = probe.RecordInstanceCreated();

    public Task HandleAsync(ScopedLifetimeCommand request)
    {
        request.HandlerInstance = _instanceId;
        return Task.CompletedTask;
    }
}

public abstract class HandlerLifetimeProbe
{
    private int _createdCount;

    public int CreatedCount => _createdCount;

    public int RecordInstanceCreated()
    {
        return Interlocked.Increment(ref _createdCount);
    }
}

public sealed class TransientHandlerProbe : HandlerLifetimeProbe
{
}

public sealed class ScopedHandlerProbe : HandlerLifetimeProbe
{
}

public abstract record MissingRequest : IRequest;

public abstract record DuplicateRequest : IRequest;

public abstract class FirstDuplicateHandler : IRequestHandler<DuplicateRequest>
{
    public abstract Task HandleAsync(DuplicateRequest request);
}

public abstract class SecondDuplicateHandler : IRequestHandler<DuplicateRequest>
{
    public abstract Task HandleAsync(DuplicateRequest request);
}

public interface ITestCancellationTokenProvider
{
    CancellationToken Token { get; }
}

internal sealed class TestCancellationTokenProvider(
    CancellationToken cancellationToken) : ITestCancellationTokenProvider
{
    public CancellationToken Token { get; } = cancellationToken;
}

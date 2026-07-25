using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Uozturk.Mediator.Dispatching;
using Xunit;

namespace Uozturk.Mediator.Tests;

public class RequestDispatcherTests
{
    [Fact]
    public async Task Should_Dispatch_Response_And_No_Response_Requests()
    {
        using var provider = BuildProvider();
        provider.ValidateUozturkMediator();
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var command = new RecordCommand();

        var response = await dispatcher.DispatchAsync(new EchoRequest("test"));
        await dispatcher.DispatchAsync(command);

        Assert.Equal("TEST", response);
        Assert.True(command.WasHandled);
    }

    [Fact]
    public async Task Should_Run_Behaviors_In_Order()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();
        var trace = new List<string>();

        await dispatcher.DispatchAsync(new OrderedRequest(trace));

        Assert.Equal(
            ["first-before", "second-before", "handler", "second-after", "first-after"],
            trace);
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

        services.AddUozturkMediator(typeof(RequestDispatcherTests).Assembly);

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(ITestCancellationTokenProvider));
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
        services.AddUozturkMediator(typeof(RequestDispatcherTests).Assembly);
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
        RequestHandlerDelegate<Unit> next)
    {
        request.Trace.Add("first-before");
        var result = await next();
        request.Trace.Add("first-after");
        return result;
    }
}

public sealed class SecondOrderedBehavior : IRequestBehavior<OrderedRequest, Unit>
{
    public int Order => 20;

    public async Task<Unit> HandleAsync(
        OrderedRequest request,
        RequestHandlerDelegate<Unit> next)
    {
        request.Trace.Add("second-before");
        var result = await next();
        request.Trace.Add("second-after");
        return result;
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

using Microsoft.CodeAnalysis;
using Xunit;

namespace UO.Mediator.Generators.Tests;

public class UOMediatorGeneratorTests
{
    [Fact]
    public void Should_Generate_Registration_For_Handlers_Executors_And_Behaviors()
    {
        const string source = """
            using System.Threading.Tasks;
            using UO.Mediator.Dispatching;

            namespace Sample;

            public sealed partial record PingRequest(int Value) : IRequest<int>;

            internal sealed class PingHandler : IRequestHandler<PingRequest, int>
            {
                public Task<int> HandleAsync(PingRequest request) =>
                    Task.FromResult(request.Value);
            }

            public sealed partial record SaveCommand : IRequest;

            public sealed class SaveHandler : IRequestHandler<SaveCommand>
            {
                public Task HandleAsync(SaveCommand request) => Task.CompletedTask;
            }

            public sealed class PingBehavior : IRequestBehavior<PingRequest, int>
            {
                public Task<int> HandleAsync(
                    PingRequest request,
                    RequestHandlerNext<PingRequest, int> next) => next.InvokeAsync();
            }
            """;

        var execution = GeneratorTestHarness.Run(source);
        var generated = GeneratorTestHarness.GetGeneratedSource(execution);

        Assert.Contains("AddSampleAppUOMediator", generated, StringComparison.Ordinal);
        Assert.Contains(
            "AddGeneratedRoutedRequest<\n            global::Sample.PingRequest,\n            int,\n            global::Sample.PingHandler>",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddGeneratedRoutedRequest<\n            global::Sample.SaveCommand,\n            global::Sample.SaveHandler>",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("AddGeneratedBehavior<", generated, StringComparison.Ordinal);
        Assert.Contains("partial record PingRequest", generated, StringComparison.Ordinal);
        Assert.Contains("IGeneratedRequestRoute<int>", generated, StringComparison.Ordinal);
        Assert.Contains("IGeneratedRequestRoute.DispatchAsync", generated, StringComparison.Ordinal);
        Assert.Contains("context.DispatchAsync", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTypes", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetType()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("MakeGenericType", generated, StringComparison.Ordinal);
        Assert.Empty(execution.RunResult.Diagnostics);
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Fact]
    public void Should_Keep_Executor_Fallback_For_NonPartial_Request()
    {
        const string source = """
            using System.Threading.Tasks;
            using UO.Mediator.Dispatching;

            public sealed record PingRequest : IRequest<int>;

            public sealed class PingHandler : IRequestHandler<PingRequest, int>
            {
                public Task<int> HandleAsync(PingRequest request) => Task.FromResult(1);
            }
            """;

        var execution = GeneratorTestHarness.Run(source);
        var generated = GeneratorTestHarness.GetGeneratedSource(execution);

        Assert.Contains("AddGeneratedRequest<", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("partial record PingRequest", generated, StringComparison.Ordinal);
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Fact]
    public void Should_Report_Duplicate_Handlers()
    {
        const string source = """
            using System.Threading.Tasks;
            using UO.Mediator.Dispatching;

            public sealed record PingRequest : IRequest<int>;

            public sealed class FirstHandler : IRequestHandler<PingRequest, int>
            {
                public Task<int> HandleAsync(PingRequest request) => Task.FromResult(1);
            }

            public sealed class SecondHandler : IRequestHandler<PingRequest, int>
            {
                public Task<int> HandleAsync(PingRequest request) => Task.FromResult(2);
            }
            """;

        var execution = GeneratorTestHarness.Run(source);
        var diagnostics = execution.RunResult.Diagnostics
            .Where(diagnostic => diagnostic.Id == "UOMG001")
            .ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void Should_Warn_When_Request_Has_No_Handler_In_Assembly()
    {
        const string source = """
            using UO.Mediator.Dispatching;

            public sealed record MissingRequest : IRequest<string>;
            """;

        var execution = GeneratorTestHarness.Run(source);
        var diagnostic = Assert.Single(
            execution.RunResult.Diagnostics.Where(candidate =>
                candidate.Id == "UOMG002"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("MissingRequest", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Report_Private_Nested_Handler()
    {
        const string source = """
            using System.Threading.Tasks;
            using UO.Mediator.Dispatching;

            public sealed record PingRequest : IRequest<int>;

            public static class Container
            {
                private sealed class PingHandler : IRequestHandler<PingRequest, int>
                {
                    public Task<int> HandleAsync(PingRequest request) => Task.FromResult(1);
                }
            }
            """;

        var execution = GeneratorTestHarness.Run(source);
        var diagnostic = Assert.Single(
            execution.RunResult.Diagnostics.Where(candidate =>
                candidate.Id == "UOMG003"));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}

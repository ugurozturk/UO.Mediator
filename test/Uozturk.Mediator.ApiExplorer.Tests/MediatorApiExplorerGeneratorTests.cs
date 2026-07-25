using Microsoft.CodeAnalysis;
using Xunit;

namespace Uozturk.Mediator.ApiExplorer.Tests;

public class MediatorApiExplorerGeneratorTests
{
    [Fact]
    public void Should_Generate_Convention_Based_Controllers_From_Referenced_Assembly()
    {
        const string requestSource = """
            using System;
            using System.Collections.Generic;
            using Uozturk.Mediator.ApiExplorer;
            using Uozturk.Mediator.Dispatching;

            namespace Contracts;

            [MediatorApiExplorer]
            public sealed record GetListBooksRequest : IRequest<IReadOnlyList<string>>;

            [MediatorApiExplorer]
            public sealed record CreateBookRequest(string Name) : IRequest<Guid>;

            [MediatorApiExplorer]
            public sealed record UpdateBookCommand(Guid Id, string Name) : IRequest;

            [MediatorApiExplorer]
            public sealed record DeleteBookRequest(Guid Id) : IRequest;

            [MediatorApiExplorer]
            public sealed record RebuildIndexCommand : IRequest;
            """;

        var requestReference = GeneratorTestHarness.CompileReference(
            requestSource,
            "Contracts");
        var execution = GeneratorTestHarness.Run(
            "public sealed class ApiHostMarker { }",
            additionalReferences: requestReference);
        var generated = GeneratorTestHarness.GetGeneratedSource(execution);

        Assert.Contains(
            "HttpGetAttribute(\"/api/app/books\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "HttpPostAttribute(\"/api/app/book\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "HttpPutAttribute(\"/api/app/book\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "HttpDeleteAttribute(\"/api/app/book\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "HttpPostAttribute(\"/api/app/rebuild-index\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("FromQueryAttribute", generated, StringComparison.Ordinal);
        Assert.Contains("FromBodyAttribute", generated, StringComparison.Ordinal);
        Assert.Contains("return NoContent();", generated, StringComparison.Ordinal);
        Assert.Equal(
            5,
            execution.RunResult.Results.SelectMany(result => result.GeneratedSources).Count());
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Fact]
    public void Should_Apply_Endpoint_And_Root_Path_Overrides()
    {
        const string source = """
            using Uozturk.Mediator.ApiExplorer;
            using Uozturk.Mediator.Dispatching;

            [MediatorApiExplorer(
                Route = "/api/catalog/rebuild",
                HttpMethod = MediatorHttpMethod.Get,
                AuthorizationPolicy = "Catalog.Read")]
            public sealed record RebuildCatalogRequest : IRequest<string>;

            [MediatorApiExplorer(AllowAnonymous = true)]
            public sealed record GetOrdersRequest : IRequest<string>;
            """;

        var execution = GeneratorTestHarness.Run(source, "/api/custom");
        var generated = GeneratorTestHarness.GetGeneratedSource(execution);

        Assert.Contains(
            "HttpGetAttribute(\"/api/catalog/rebuild\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "AuthorizeAttribute(Policy = \"Catalog.Read\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "HttpGetAttribute(\"/api/custom/orders\")",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "AllowAnonymousAttribute",
            generated,
            StringComparison.Ordinal);
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Fact]
    public void Should_Report_Invalid_Requests_And_Endpoint_Conflicts()
    {
        const string source = """
            using Uozturk.Mediator.ApiExplorer;
            using Uozturk.Mediator.Dispatching;

            [MediatorApiExplorer]
            public sealed class NotARequest;

            [MediatorApiExplorer]
            internal sealed record CreateHiddenRequest : IRequest;

            [MediatorApiExplorer]
            public sealed record CreateBookRequest : IRequest;

            [MediatorApiExplorer]
            public sealed record PostBookCommand : IRequest;

            [MediatorApiExplorer(Route = "/api/books/{id}")]
            public sealed record GetBookRequest : IRequest<string>;

            [MediatorApiExplorer(
                AuthorizationPolicy = "Books.Read",
                AllowAnonymous = true)]
            public sealed record GetAuthorsRequest : IRequest<string>;
            """;

        var execution = GeneratorTestHarness.Run(source);
        var diagnosticIds = execution.RunResult.Diagnostics
            .Select(diagnostic => diagnostic.Id)
            .ToArray();

        Assert.Contains("UOMA002", diagnosticIds);
        Assert.Contains("UOMA003", diagnosticIds);
        Assert.Contains("UOMA004", diagnosticIds);
        Assert.Contains("UOMA005", diagnosticIds);
    }

    [Fact]
    public void Should_Keep_Same_Simple_Type_Names_Unique_By_Namespace()
    {
        const string source = """
            using Uozturk.Mediator.ApiExplorer;
            using Uozturk.Mediator.Dispatching;

            namespace First
            {
                [MediatorApiExplorer(Route = "/api/first/ping")]
                public sealed record PingRequest : IRequest<string>;
            }

            namespace Second
            {
                [MediatorApiExplorer(Route = "/api/second/ping")]
                public sealed record PingRequest : IRequest<string>;
            }
            """;

        var execution = GeneratorTestHarness.Run(source);
        var hintNames = execution.RunResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.HintName)
            .ToArray();

        Assert.Equal(2, hintNames.Distinct(StringComparer.Ordinal).Count());
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }
}

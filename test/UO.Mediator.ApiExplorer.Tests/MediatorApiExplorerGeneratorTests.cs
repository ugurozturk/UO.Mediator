using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace UO.Mediator.ApiExplorer.Tests;

public class MediatorApiExplorerGeneratorTests
{
    [Fact]
    public void Should_Generate_Convention_Based_Controllers_From_Referenced_Assembly()
    {
        const string requestSource = """
            using System;
            using System.Collections.Generic;
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

            namespace Contracts;

            [MediatorApiExplorer(ControllerName = "Book")]
            public sealed record GetListBooksRequest : IRequest<IReadOnlyList<string>>;

            [MediatorApiExplorer(ControllerName = "Book")]
            public sealed record CreateBookRequest(string Name) : IRequest<Guid>;

            [MediatorApiExplorer(ControllerName = "Book")]
            public sealed record UpdateBookCommand(Guid Id, string Name) : IRequest;

            [MediatorApiExplorer(ControllerName = "Book")]
            public sealed record DeleteBookRequest(Guid Id) : IRequest;

            [MediatorApiExplorer(ControllerName = "Maintenance")]
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
        Assert.Contains("public partial class BookController", generated, StringComparison.Ordinal);
        Assert.Contains(
            "BookController : global::Microsoft.AspNetCore.Mvc.ControllerBase",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("TagsAttribute(\"Book\")", generated, StringComparison.Ordinal);
        Assert.Contains("CreateBookAsync(", generated, StringComparison.Ordinal);
        Assert.Contains("GetListBooksAsync(", generated, StringComparison.Ordinal);
        Assert.Contains("UpdateBookAsync(", generated, StringComparison.Ordinal);
        Assert.Contains("DeleteBookAsync(", generated, StringComparison.Ordinal);
        Assert.Contains("return NoContent();", generated, StringComparison.Ordinal);
        Assert.Equal(
            2,
            execution.RunResult.Results.SelectMany(result => result.GeneratedSources).Count());
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Fact]
    public void Should_Inherit_From_Configured_Controller_Base()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

            namespace CustomApi;

            public abstract class CustomControllerBase : ControllerBase
            {
                public string Source => "Custom";
            }

            [MediatorApiExplorer(ControllerName = "Book")]
            public sealed record GetBookRequest : IRequest<string>;
            """;

        var execution = GeneratorTestHarness.Run(
            source,
            controllerBase: "global::CustomApi.CustomControllerBase");
        var generated = GeneratorTestHarness.GetGeneratedSource(execution);

        Assert.Contains(
            "BookController : global::CustomApi.CustomControllerBase",
            generated,
            StringComparison.Ordinal);
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Fact]
    public void Should_Use_Default_Controller_Base_When_Build_Property_Is_Empty()
    {
        const string source = """
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

            [MediatorApiExplorer]
            public sealed record GetBookRequest : IRequest<string>;
            """;

        var execution = GeneratorTestHarness.Run(source, controllerBase: "");
        var generated = GeneratorTestHarness.GetGeneratedSource(execution);

        Assert.Contains(
            "BookController : global::Microsoft.AspNetCore.Mvc.ControllerBase",
            generated,
            StringComparison.Ordinal);
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Theory]
    [InlineData("Missing.CustomControllerBase", "could not be found")]
    [InlineData("System.Object", "must inherit")]
    [InlineData("System.String", "cannot be sealed")]
    [InlineData(" ", "cannot be empty")]
    public void Should_Report_Invalid_Configured_Controller_Base(
        string controllerBase,
        string expectedMessage)
    {
        const string source = """
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

            [MediatorApiExplorer]
            public sealed record GetBookRequest : IRequest<string>;
            """;

        var execution = GeneratorTestHarness.Run(
            source,
            controllerBase: controllerBase);
        var diagnostic = Assert.Single(
            execution.RunResult.Diagnostics.Where(candidate =>
                candidate.Id == "UOMA007"));

        Assert.Contains(expectedMessage, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(
            execution.RunResult.Results.SelectMany(result => result.GeneratedSources));
    }

    [Fact]
    public void Should_Apply_Endpoint_And_Root_Path_Overrides()
    {
        const string source = """
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

            [MediatorApiExplorer(
                ControllerName = "Catalog",
                Route = "/api/catalog/rebuild",
                HttpMethod = MediatorHttpMethod.Get,
                AuthorizationPolicy = "Catalog.Read")]
            public sealed record RebuildCatalogRequest : IRequest<string>;

            [MediatorApiExplorer(ControllerName = "Catalog", AllowAnonymous = true)]
            public sealed record GetOrdersRequest : IRequest<string>;

            namespace UO.Mediator.ApiExplorer.Generated
            {
                public partial class AppCatalogServiceController
                {
                    public const string ExtendedByConsumer = "yes";
                }
            }
            """;

        var execution = GeneratorTestHarness.Run(
            source,
            "/api/custom",
            "App",
            "Service");
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
        Assert.Contains(
            "public partial class AppCatalogServiceController",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "RebuildCatalogAsync(",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "GetOrdersAsync(",
            generated,
            StringComparison.Ordinal);
        Assert.Single(
            execution.RunResult.Results.SelectMany(result => result.GeneratedSources));
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Fact]
    public void Should_Copy_Request_Attributes_From_Referenced_Assembly_To_Action()
    {
        const string requestSource = """
            using System;
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

            namespace Contracts;

            public enum MetadataLevel
            {
                Low = 1,
                High = 2
            }

            [AttributeUsage(
                AttributeTargets.Class | AttributeTargets.Method,
                AllowMultiple = true)]
            public sealed class EndpointMetadataAttribute : Attribute
            {
                public EndpointMetadataAttribute(
                    string name,
                    Type responseType,
                    MetadataLevel level,
                    int[] codes)
                {
                }

                public bool Enabled { get; set; }

                public char Separator { get; set; }
            }

            public sealed class DefaultMetadataAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ClassOnlyMetadataAttribute : Attribute;

            [MediatorApiExplorer(ControllerName = "Book")]
            [EndpointMetadata(
                "books\napi",
                typeof(string),
                MetadataLevel.High,
                new[] { 1, 2 },
                Enabled = true,
                Separator = '|')]
            [DefaultMetadata]
            [ClassOnlyMetadata]
            public sealed record GetBookRequest : IRequest<string>;
            """;

        var requestReference = GeneratorTestHarness.CompileReference(
            requestSource,
            "ContractsWithMetadata");
        var execution = GeneratorTestHarness.Run(
            "public sealed class ApiHostMarker { }",
            additionalReferences: requestReference);
        var generated = GeneratorTestHarness.GetGeneratedSource(execution);
        var root = CSharpSyntaxTree.ParseText(generated).GetRoot();
        var controller = Assert.Single(
            root.DescendantNodes().OfType<ClassDeclarationSyntax>());
        var action = Assert.Single(
            controller.Members.OfType<MethodDeclarationSyntax>());
        var controllerAttributes = controller.AttributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => attribute.Name.ToString())
            .ToArray();
        var actionAttributes = action.AttributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => attribute.Name.ToString())
            .ToArray();

        Assert.DoesNotContain(
            controllerAttributes,
            name => name.Contains("EndpointMetadataAttribute", StringComparison.Ordinal));
        Assert.Contains(
            actionAttributes,
            name => name.Contains("EndpointMetadataAttribute", StringComparison.Ordinal));
        Assert.Contains(
            actionAttributes,
            name => name.Contains("DefaultMetadataAttribute", StringComparison.Ordinal));
        Assert.DoesNotContain("ClassOnlyMetadataAttribute", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("MediatorApiExplorerAttribute", generated, StringComparison.Ordinal);
        Assert.Contains("\"books\\napi\"", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(string)", generated, StringComparison.Ordinal);
        Assert.Contains(
            "(global::Contracts.MetadataLevel)2",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("new int[] { 1, 2 }", generated, StringComparison.Ordinal);
        Assert.Contains("Enabled = true", generated, StringComparison.Ordinal);
        Assert.Contains("Separator = '|'", generated, StringComparison.Ordinal);
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }

    [Fact]
    public void Should_Report_Invalid_Requests_And_Endpoint_Conflicts()
    {
        const string source = """
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

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

            [MediatorApiExplorer(ControllerName = "Invalid Name")]
            public sealed record GetPublishersRequest : IRequest<string>;
            """;

        var execution = GeneratorTestHarness.Run(source);
        var diagnosticIds = execution.RunResult.Diagnostics
            .Select(diagnostic => diagnostic.Id)
            .ToArray();

        Assert.Contains("UOMA002", diagnosticIds);
        Assert.Contains("UOMA003", diagnosticIds);
        Assert.Contains("UOMA004", diagnosticIds);
        Assert.Contains("UOMA005", diagnosticIds);
        Assert.Contains("UOMA007", diagnosticIds);
    }

    [Fact]
    public void Should_Group_Same_Controller_Name_And_Keep_Overloaded_Actions_Valid()
    {
        const string source = """
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

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
        var generatedSources = execution.RunResult.Results
            .SelectMany(result => result.GeneratedSources)
            .ToArray();
        var generated = GeneratorTestHarness.GetGeneratedSource(execution);

        Assert.Single(generatedSources);
        Assert.Contains(
            "public partial class PingController",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::First.PingRequest request",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Second.PingRequest request",
            generated,
            StringComparison.Ordinal);
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator;
using Xunit;

namespace UO.Mediator.ApiExplorer.Tests;

public class MediatorApiExplorerIntegrationTests
{
    [Fact]
    public async Task Generated_Controllers_Should_Dispatch_Through_Real_Mvc_Host()
    {
        const string source = """
            using System.Threading.Tasks;
            using UO.Mediator.ApiExplorer;
            using UO.Mediator.Dispatching;

            namespace IntegrationContracts;

            [MediatorApiExplorer(ControllerName = "Catalog")]
            public sealed partial record GetGreetingRequest(string Name) : IRequest<string>;

            public sealed class GetGreetingHandler
                : IRequestHandler<GetGreetingRequest, string>
            {
                public Task<string> HandleAsync(GetGreetingRequest request)
                {
                    return Task.FromResult($"Hello, {request.Name}!");
                }
            }

            [MediatorApiExplorer(ControllerName = "Catalog")]
            public sealed partial record RebuildCatalogCommand : IRequest;

            public sealed class RebuildCatalogHandler
                : IRequestHandler<RebuildCatalogCommand>
            {
                public Task HandleAsync(RebuildCatalogCommand request)
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var execution = GeneratorTestHarness.RunWithMediatorRegistration(source);
        GeneratorTestHarness.AssertNoCompilationErrors(execution);
        var generatedSource = GeneratorTestHarness.GetGeneratedSource(execution);
        Assert.Equal(
            3,
            execution.RunResult.Results.SelectMany(result => result.GeneratedSources).Count());
        Assert.Contains(
            "public partial class CatalogController",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddGeneratedRoutedRequest<",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "IGeneratedRequestRoute<string>",
            generatedSource,
            StringComparison.Ordinal);
        var generatedAssembly = GeneratorTestHarness.EmitAndLoad(execution);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddControllers()
            .AddApplicationPart(generatedAssembly);
        InvokeGeneratedRegistration(generatedAssembly, builder.Services);

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        var client = app.GetTestClient();
        var greetingResponse = await client.GetAsync(
            "/api/app/greeting?Name=Ada");
        var greeting = await greetingResponse.Content.ReadFromJsonAsync<string>();
        var rebuildResponse = await client.PostAsJsonAsync(
            "/api/app/rebuild-catalog",
            new { });

        Assert.Equal(HttpStatusCode.OK, greetingResponse.StatusCode);
        Assert.Equal("Hello, Ada!", greeting);
        Assert.Equal(HttpStatusCode.NoContent, rebuildResponse.StatusCode);

        var apiExplorer = app.Services
            .GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        var descriptions = apiExplorer.ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .ToArray();

        Assert.Contains(
            descriptions,
            description =>
                description.HttpMethod == "GET" &&
                description.RelativePath == "api/app/greeting");
        Assert.Contains(
            descriptions,
            description =>
                description.HttpMethod == "POST" &&
                description.RelativePath == "api/app/rebuild-catalog");
    }

    private static void InvokeGeneratedRegistration(
        System.Reflection.Assembly generatedAssembly,
        IServiceCollection services)
    {
        var registrationType = Assert.Single(generatedAssembly.GetTypes(), type =>
            type.Namespace == "UO.Mediator.Generated" &&
            type.Name.EndsWith("UOMediatorRegistration", StringComparison.Ordinal));
        var registrationMethod = Assert.Single(registrationType.GetMethods(), method =>
            method.Name.StartsWith("Add", StringComparison.Ordinal) &&
            method.Name.EndsWith("UOMediator", StringComparison.Ordinal));

        registrationMethod.Invoke(null, [services, null]);
    }
}

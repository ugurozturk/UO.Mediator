using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator.Dispatching;
using Xunit;

namespace UO.Mediator.Generators.Tests;

internal static class GeneratorTestHarness
{
    public static GeneratorExecution Run(
        string source,
        string assemblyName = "Sample.App")
    {
        var compilation = CreateCompilation(source, assemblyName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new UOMediatorGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.Single().Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        return new GeneratorExecution(
            (CSharpCompilation)outputCompilation,
            driver.GetRunResult(),
            generatorDiagnostics);
    }

    public static string GetGeneratedSource(GeneratorExecution execution)
    {
        return string.Join(
            Environment.NewLine,
            execution.RunResult.Results
                .SelectMany(result => result.GeneratedSources)
                .Select(source => source.SourceText.ToString()));
    }

    public static void AssertNoCompilationErrors(GeneratorExecution execution)
    {
        var errors = execution.OutputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(errors);
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var references = GetPlatformReferences()
            .Append(MetadataReference.CreateFromFile(typeof(IRequest).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(
                typeof(UOMediatorServiceCollectionExtensions).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static IEnumerable<MetadataReference> GetPlatformReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");

        Assert.False(string.IsNullOrWhiteSpace(trustedPlatformAssemblies));

        return trustedPlatformAssemblies!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}

internal sealed record GeneratorExecution(
    CSharpCompilation OutputCompilation,
    GeneratorDriverRunResult RunResult,
    System.Collections.Immutable.ImmutableArray<Diagnostic> GeneratorDiagnostics);

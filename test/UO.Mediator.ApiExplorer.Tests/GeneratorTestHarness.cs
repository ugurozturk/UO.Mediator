using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using UO.Mediator;
using UO.Mediator.ApiExplorer.Generators;
using UO.Mediator.Dispatching;
using UO.Mediator.Generators;
using Xunit;

namespace UO.Mediator.ApiExplorer.Tests;

internal static class GeneratorTestHarness
{
    public static GeneratorExecution Run(
        string source,
        string rootPath = "/api/app",
        string controllerPrefix = "",
        string controllerSuffix = "",
        string controllerBase = "Microsoft.AspNetCore.Mvc.ControllerBase",
        params MetadataReference[] additionalReferences)
    {
        return RunCore(
            source,
            includeMediatorRegistration: false,
            rootPath,
            controllerPrefix,
            controllerSuffix,
            controllerBase,
            additionalReferences);
    }

    public static GeneratorExecution RunWithMediatorRegistration(
        string source,
        string rootPath = "/api/app",
        string controllerPrefix = "",
        string controllerSuffix = "",
        string controllerBase = "Microsoft.AspNetCore.Mvc.ControllerBase",
        params MetadataReference[] additionalReferences)
    {
        return RunCore(
            source,
            includeMediatorRegistration: true,
            rootPath,
            controllerPrefix,
            controllerSuffix,
            controllerBase,
            additionalReferences);
    }

    private static GeneratorExecution RunCore(
        string source,
        bool includeMediatorRegistration,
        string rootPath,
        string controllerPrefix,
        string controllerSuffix,
        string controllerBase,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CreateCompilation(
            source,
            "ApiHost_" + Guid.NewGuid().ToString("N"),
            additionalReferences);
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.Single().Options;
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_property.UOMediatorApiRootPath"] = rootPath,
                ["build_property.UOMediatorControllerPrefix"] = controllerPrefix,
                ["build_property.UOMediatorControllerSuffix"] = controllerSuffix,
                ["build_property.UOMediatorControllerBase"] = controllerBase
            });

        var generators = includeMediatorRegistration
            ? new ISourceGenerator[]
            {
                new MediatorApiExplorerGenerator().AsSourceGenerator(),
                new UOMediatorGenerator().AsSourceGenerator()
            }
            : [new MediatorApiExplorerGenerator().AsSourceGenerator()];
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: generators,
            additionalTexts: [],
            parseOptions: parseOptions,
            optionsProvider: optionsProvider);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        return new GeneratorExecution(
            (CSharpCompilation)outputCompilation,
            driver.GetRunResult(),
            generatorDiagnostics);
    }

    public static PortableExecutableReference CompileReference(
        string source,
        string assemblyName)
    {
        var compilation = CreateCompilation(source, assemblyName);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    public static Assembly EmitAndLoad(GeneratorExecution execution)
    {
        using var stream = new MemoryStream();
        var result = execution.OutputCompilation.Emit(stream);

        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)));

        stream.Position = 0;
        return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(stream);
    }

    public static string GetGeneratedSource(GeneratorExecution execution)
    {
        return string.Join(
            Environment.NewLine,
            execution.RunResult.Results.SelectMany(result => result.GeneratedSources)
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
        string assemblyName,
        params MetadataReference[] additionalReferences)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var references = GetPlatformReferences()
            .Append(MetadataReference.CreateFromFile(typeof(IRequest).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(
                typeof(UOMediatorServiceCollectionExtensions).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location))
            .Concat(additionalReferences)
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

    private sealed class TestAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues)
        : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions =
            new TestAnalyzerConfigOptions(globalValues);
        private static readonly AnalyzerConfigOptions EmptyOptions =
            new TestAnalyzerConfigOptions(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return EmptyOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return EmptyOptions;
        }
    }

    private sealed class TestAnalyzerConfigOptions(
        IReadOnlyDictionary<string, string> values)
        : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            return values.TryGetValue(key, out value!);
        }
    }
}

internal sealed record GeneratorExecution(
    CSharpCompilation OutputCompilation,
    GeneratorDriverRunResult RunResult,
    ImmutableArray<Diagnostic> GeneratorDiagnostics);

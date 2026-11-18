using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Concord.Generators.Tests;

internal static class GeneratorTestHarness {
    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static IReadOnlyList<MetadataReference> BuildReferences() {
        Assembly[] forceLoaded = [
            typeof(Concord.PatchAttribute).Assembly,
            typeof(Concord.InjectAttribute).Assembly,
            typeof(Concord.EnumMemberAttribute).Assembly,
            typeof(Concord.IPatchManifestSink).Assembly,
        ];
        HashSet<string> locations = [];
        List<MetadataReference> references = [];
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Concat(forceLoaded)) {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location) && locations.Add(assembly.Location)) {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        return references;
    }

    public static (Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics) Run(IIncrementalGenerator generator, string source) {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestMod",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (output, diagnostics);
    }

    public static (Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics) Compile(
        IReadOnlyList<(string Path, string Source)> sources,
        bool attributesInMetadata,
        IReadOnlyList<MetadataReference>? extraReferences = null,
        string? assemblyName = null,
        LanguageVersion languageVersion = LanguageVersion.Latest) {
        CSharpParseOptions options = new CSharpParseOptions(languageVersion);
        if (attributesInMetadata) {
            options = options.WithPreprocessorSymbols("CONCORD_ATTRIBUTES_IN_METADATA");
        }

        List<SyntaxTree> trees = [];
        foreach ((string path, string source) in sources) {
            trees.Add(CSharpSyntaxTree.ParseText(source, options, path));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName ?? "ManifestMod_" + Guid.NewGuid().ToString("N"),
            trees,
            References.Concat(extraReferences ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(
            [
                new PatchManifestGenerator().AsSourceGenerator(),
                new PatchRegistryGenerator().AsSourceGenerator(),
                new ShadowMemberGenerator().AsSourceGenerator(),
            ],
            parseOptions: options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (output, diagnostics);
    }

    public static byte[] Emit(Compilation output) {
        using MemoryStream image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = output.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return image.ToArray();
    }

    public static Assembly Load(Compilation output) {
        return Assembly.Load(Emit(output));
    }

    public static string GeneratedSource(Compilation output, string hintSubstring) {
        SyntaxTree? tree = null;
        foreach (SyntaxTree candidate in output.SyntaxTrees) {
            if (candidate.FilePath.Contains(hintSubstring)) {
                tree = candidate;
                break;
            }
        }

        Assert.NotNull(tree);
        return tree!.ToString();
    }

    public static void AssertCompiles(Compilation output) {
        List<Diagnostic> errors = [];
        foreach (Diagnostic diagnostic in output.GetDiagnostics()) {
            if (diagnostic.Severity == DiagnosticSeverity.Error) {
                errors.Add(diagnostic);
            }
        }

        Assert.Empty(errors);
    }
}

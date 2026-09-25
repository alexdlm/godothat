using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ObservableCollections;
using R3;

namespace GodotHat.Binding.SourceGenerators.Test;

public static class GeneratorHarness
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    [ModuleInitializer]
    internal static void Initialize() => VerifySourceGenerators.Initialize();

    public static CSharpCompilation Compile(params string[] sources) =>
        CSharpCompilation.Create(
            "ViewModels",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, ParseOptions, path: $"Source{i}.cs")),
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static GeneratorDriver Run(CSharpCompilation compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ViewModelGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        return driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out diagnostics);
    }

    public static GeneratorDriver Run(params string[] sources) => Run(Compile(sources), out _, out _);

    // Compiles the sources with generated accessors, loads the assembly and runs its module initializers, which
    // register the accessors.
    public static Assembly CompileAndLoad(params string[] sources)
    {
        Run(Compile(sources), out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = output.Emit(stream);
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(stream.ToArray()));
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return assembly;
    }

    public static IEnumerable<Diagnostic> ErrorsIn(Compilation compilation) =>
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

    private static readonly Lazy<MetadataReference[]> References = new(() =>
    {
        // Load the assemblies that view models use, so they are found below
        _ = typeof(ReactiveProperty<>);
        _ = typeof(ObservableList<>);
        _ = typeof(BindingRegistry);

        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.Location))
            .Where(location => !string.IsNullOrEmpty(location))
            .Distinct()
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToArray();
    });
}

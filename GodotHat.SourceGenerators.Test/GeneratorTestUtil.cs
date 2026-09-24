using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GodotHat.SourceGenerators.Test;

public static class GeneratorTestUtil
{
    // Despite being a required assembly it won't be available unless used, ensure Godot & our Attributes are used
    // before GetAssemblies() is called.
    [SceneUniqueName("%foo")]
    public static Color ForceAssembliesToBeLoaded { get; set; }

    [OnEnterTree]
    [OnExitTree]
    [OnReady]
    public static Color ForceAssembliesToBeLoadedFoo() => ForceAssembliesToBeLoaded;

    public static (Compilation compilation, IEnumerable<Diagnostic> diagnostics) RunGeneratorCompilation<T>(
        T generator,
        params SyntaxTree[] syntaxTrees) where T : IIncrementalGenerator =>
        RunGeneratorsCompilation([generator], syntaxTrees);

    public static (Compilation compilation, IEnumerable<Diagnostic> diagnostics) RunGeneratorsCompilation(
        IIncrementalGenerator[] generators,
        params SyntaxTree[] syntaxTrees)
    {
        var compilation = CSharpCompilation.Create(
            "testGen",
            syntaxTrees,
            GetAssemblyReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var driver = CSharpGeneratorDriver.Create(generators);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out var diagnostics);

        return (outputCompilation, diagnostics);
    }

    public static IEnumerable<Diagnostic> GetGeneratedCodeErrors(Compilation compilation, params SyntaxTree[] inputTrees) =>
        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Location.SourceTree is { } tree && !inputTrees.Contains(tree));

    public static IEnumerable<MetadataReference> GetAssemblyReferences()
    {
        var platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .Select(assembly => assembly.Location);
        return platformAssemblies
            .Concat(loadedAssemblies)
            .Where(location => !string.IsNullOrEmpty(location))
            .Distinct()
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location));
    }
}

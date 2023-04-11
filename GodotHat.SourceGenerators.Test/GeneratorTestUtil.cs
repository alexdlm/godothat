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
        params SyntaxTree[] syntaxTrees) where T : IIncrementalGenerator
    {
        var compilation = CSharpCompilation.Create(
            "testGen",
            syntaxTrees,
            GetAssemblyReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out var diagnostics);

        return (outputCompilation, diagnostics);
    }

    public static IEnumerable<MetadataReference> GetAssemblyReferences()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        return assemblies
            .Where(assembly => !assembly.IsDynamic)
            .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location));
    }
}

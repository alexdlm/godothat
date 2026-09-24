using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GodotHat.SourceGenerators.Test;

public class ScriptMethodsMarshallingTest
{
    private const string OtherNamespaceSource = @"
namespace Test.Other;

public enum Kind { A, B }
";

    private const string MyNodeSource = @"
namespace Test.Node;

using System;
using Godot;
using Godot.Collections;
using GodotHat;

public partial class MyNode : Node
{
    [OnReady]
    private IDisposable? DoReady() => null;

    [OnEnterTree]
    private void DoEnterTree() {}

    [AutoDispose]
    private IDisposable Foo(Node node) => new System.IO.MemoryStream();

    [SceneUniqueName]
    private Camera2D TheCamera = null!;

    public int CountNodes(Array<Node> nodes) => nodes.Count;
    public Dictionary<string, int> GetScores() => new();
    public void TakeUntypedArray(Godot.Collections.Array items) {}
    public void TakeColors(Color[] colors) {}
    public void TakeVectors(Vector4[] vectors) {}
    public Node[] GetChildNodes() => [];
    public void TakeKind(Test.Other.Kind kind) {}
    public string[] GetStrings() => [];
    public StringName TakeNames(StringName name, NodePath path) => name;
}

public partial class Outer : Node
{
    public partial class Inner : Node
    {
        [OnReady]
        private void DoReady() {}

        public float Half(float value) => value / 2;
    }
}
";

    public ScriptMethodsMarshallingTest()
    {
        GeneratorTestUtil.ForceAssembliesToBeLoadedFoo();
    }

    private static (Compilation Compilation, SyntaxTree[] InputTrees) Generate(params IIncrementalGenerator[] generators)
    {
        SyntaxTree[] inputTrees =
        [
            CSharpSyntaxTree.ParseText(MyNodeSource),
            CSharpSyntaxTree.ParseText(OtherNamespaceSource),
        ];

        (Compilation outputCompilation, var diagnostics) =
            GeneratorTestUtil.RunGeneratorsCompilation(generators, inputTrees);
        diagnostics.Should().BeEmpty();
        return (outputCompilation, inputTrees);
    }

    private static string GetGeneratedSource(Compilation compilation, string fileName) =>
        compilation.SyntaxTrees.Single(tree => tree.FilePath.EndsWith(fileName)).ToString();

    [Fact]
    public void GeneratedCodeCompiles()
    {
        (Compilation compilation, SyntaxTree[] inputTrees) = Generate(
            new ScriptMethodsGenerator(),
            new OnReadyGenerator(),
            new OnEnterTreeGenerator(),
            new OnExitTreeGenerator());

        GeneratorTestUtil.GetGeneratedCodeErrors(compilation, inputTrees).Should().BeEmpty();
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Theory]
    [InlineData("CountNodes", "global::Godot.NativeInterop.VariantUtils.ConvertTo<global::Godot.Collections.Array<global::Godot.Node>>(args[0])")]
    [InlineData("TakeUntypedArray", "global::Godot.NativeInterop.VariantUtils.ConvertTo<global::Godot.Collections.Array>(args[0])")]
    [InlineData("TakeColors", "global::Godot.NativeInterop.VariantUtils.ConvertTo<global::Godot.Color[]>(args[0])")]
    [InlineData("TakeVectors", "global::Godot.NativeInterop.VariantUtils.ConvertTo<global::Godot.Vector4[]>(args[0])")]
    [InlineData("TakeKind", "global::Godot.NativeInterop.VariantUtils.ConvertTo<global::Test.Other.Kind>(args[0])")]
    [InlineData("TakeNames", "global::Godot.NativeInterop.VariantUtils.ConvertTo<global::Godot.NodePath>(args[1])")]
    public void ConvertsArgumentsWithQualifiedTypes(string methodName, string expectedConversion)
    {
        (Compilation compilation, _) = Generate(new ScriptMethodsGenerator());
        string output = GetGeneratedSource(compilation, "Test.Node.MyNode_ScriptMethods.generated.cs");

        output.Should().Contain($"if (method == MethodName.{methodName} &&");
        output.Should().Contain(expectedConversion);
    }

    [Theory]
    [InlineData("CountNodes", "global::Godot.NativeInterop.VariantUtils.CreateFrom<int>(callRet)")]
    [InlineData("GetScores", "global::Godot.NativeInterop.VariantUtils.CreateFrom<global::Godot.Collections.Dictionary<string, int>>(callRet)")]
    [InlineData("GetChildNodes", "global::Godot.NativeInterop.VariantUtils.CreateFromSystemArrayOfGodotObject(callRet)")]
    [InlineData("GetStrings", "global::Godot.NativeInterop.VariantUtils.CreateFrom<string[]>(callRet)")]
    [InlineData("TakeNames", "global::Godot.NativeInterop.VariantUtils.CreateFrom<global::Godot.StringName>(callRet)")]
    public void ReturnsValues(string methodName, string expectedReturn)
    {
        (Compilation compilation, _) = Generate(new ScriptMethodsGenerator());
        string output = GetGeneratedSource(compilation, "Test.Node.MyNode_ScriptMethods.generated.cs");

        output.Should().Contain($"var callRet = {methodName}(");
        output.Should().Contain($"ret = {expectedReturn};");
    }

    [Theory]
    [InlineData("TakeColors", "PackedColorArray")]
    [InlineData("TakeVectors", "PackedVector4Array")]
    [InlineData("TakeNames", "StringName")]
    public void UsesExpectedArgumentVariantTypes(string methodName, string expectedVariantType)
    {
        (Compilation compilation, _) = Generate(new ScriptMethodsGenerator());
        string output = GetGeneratedSource(compilation, "Test.Node.MyNode_ScriptMethods.generated.cs");

        string methodInfo = output[output.IndexOf($"MethodInfo {methodName} = new(", StringComparison.Ordinal)..];
        methodInfo = methodInfo[..methodInfo.IndexOf(");", StringComparison.Ordinal)];
        methodInfo.Should().Contain($"arguments: new() {{\n                    new (\n                        type: global::Godot.Variant.Type.{expectedVariantType},".ReplaceLineEndings());
    }

    [Fact]
    public void GeneratesNestedClassesInsideContainingTypes()
    {
        (Compilation compilation, SyntaxTree[] inputTrees) =
            Generate(new ScriptMethodsGenerator(), new OnReadyGenerator(), new OnExitTreeGenerator());

        GetGeneratedSource(compilation, "Test.Node.Outer.Inner_ScriptMethods.generated.cs")
            .Should()
            .Contain("partial class Outer\n{\npublic partial class Inner\n".ReplaceLineEndings());
        GetGeneratedSource(compilation, "Test.Node.Outer.Inner_OnReady.generated.cs")
            .Should()
            .Contain("partial class Outer\n{\npublic partial class Inner\n".ReplaceLineEndings());
        GeneratorTestUtil.GetGeneratedCodeErrors(compilation, inputTrees).Should().BeEmpty();
    }
}

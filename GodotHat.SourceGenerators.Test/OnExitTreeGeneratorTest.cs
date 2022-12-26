using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace GodotHat.SourceGenerators.Test;

public class OnExitTreeGeneratorTest
{
    private readonly ITestOutputHelper testOutputHelper;

    private const string MyNodeSource = $@"
namespace Test.Node;

using System;
using alecs.Core;
using Godot;
using GodotHat;

public partial class MyNode : Node
{{
    [OnEnterTree]
    private IDisposable DoThing()
    {{
        return new List<string>().Select(x => x).GetEnumerator(); 
    }}

    [OnEnterTree]
    private IDisposable? DoThing2()
    {{
        return new List<string>().Select(x => x).GetEnumerator(); 
    }}
}}
";

    public OnExitTreeGeneratorTest(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        GeneratorTestUtil.ForceAssembliesToBeLoadedFoo();
    }

    [Fact]
    public void GeneratesMembers()
    {
        SyntaxTree syntaxIdComponent = CSharpSyntaxTree.ParseText(MyNodeSource);

        (Compilation? outputCompilation, var diagnostics) =
            GeneratorTestUtil.RunGeneratorCompilation(new OnExitTreeGenerator(), syntaxIdComponent);
        diagnostics.Should().BeEmpty();

        string? output = outputCompilation.SyntaxTrees
            .Single(tree => tree.FilePath.EndsWith("Test.Node.MyNode_OnExitTree.generated.cs"))
            ?.ToString();

        output.Should()
            .Be(
                @"// Generated code via GodotHat.SourceGenerators.OnExitTreeGenerator
namespace Test.Node;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

#nullable enable

public partial class MyNode
{

    public override void _ExitTree()
    {
        // Generated code, to add other calls add [OnExitTree] attributes to methods

        __DisposeOnExitTree();
    }

    private void __DisposeOnExitTree()
    {
        __Dispose_DoThing2();
        __Dispose_DoThing();
    }
}
");
    }

}

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace GodotHat.SourceGenerators.Test;

public class OnEnterTreeGeneratorTest
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
    private void DoThing2()
    {{
    }}


    [OnEnterTree]
    private IDisposable? DoThing3()
    {{
        return new List<string>().Select(x => x).GetEnumerator(); 
    }}
}}
";

    public OnEnterTreeGeneratorTest(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        GeneratorTestUtil.ForceAssembliesToBeLoadedFoo();
    }

    [Fact]
    public void GeneratesMembers()
    {
        SyntaxTree syntaxIdComponent = CSharpSyntaxTree.ParseText(MyNodeSource);

        (Compilation? outputCompilation, var diagnostics) =
            GeneratorTestUtil.RunGeneratorCompilation(new OnEnterTreeGenerator(), syntaxIdComponent);
        diagnostics.Should().BeEmpty();

        string? output = outputCompilation.SyntaxTrees
            .Single(tree => tree.FilePath.EndsWith("Test.Node.MyNode_OnEnterTree.generated.cs"))
            ?.ToString();

        output.Should()
            .Be(
                @"// Generated code via GodotHat.SourceGenerators.OnEnterTreeGenerator
namespace Test.Node;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

#nullable enable

public partial class MyNode
{

    public override void _EnterTree()
    {
        // Generated code, to add other calls add [OnEnterTree] attributes to methods

        __disposable_DoThing = DoThing();
        DoThing2();
        __disposable_DoThing3 = DoThing3();
    }
}
");
    }

}

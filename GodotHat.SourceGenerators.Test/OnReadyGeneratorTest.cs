using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace GodotHat.SourceGenerators.Test;

public class OnReadyGeneratorTest
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
    [OnReady]
    private IDisposable DoThing()
    {{
        return new List<string>().Select(x => x).GetEnumerator(); 
    }}

    [OnReady]
    private void DoThing2()
    {{
    }}


    [OnReady]
    [AutoDispose]
    private IDisposable? DoThing3()
    {{
        return new List<string>().Select(x => x).GetEnumerator(); 
    }}
}}
";

    public OnReadyGeneratorTest(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        GeneratorTestUtil.ForceAssembliesToBeLoadedFoo();
    }

    [Fact]
    public void GeneratesMembers()
    {
        SyntaxTree syntaxIdComponent = CSharpSyntaxTree.ParseText(MyNodeSource);

        (Compilation? outputCompilation, var diagnostics) =
            GeneratorTestUtil.RunGeneratorCompilation(new OnReadyGenerator(), syntaxIdComponent);
        diagnostics.Should().BeEmpty();

        string? output = outputCompilation.SyntaxTrees
            .Single(tree => tree.FilePath.EndsWith("Test.Node.MyNode_OnReady.generated.cs"))
            ?.ToString();

        output.Should()
            .Be(
                @"// Generated code via GodotHat.SourceGenerators.OnReadyGenerator
namespace Test.Node;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Godot;

#nullable enable

public partial class MyNode
{
    public override void _Ready()
    {
        // Generated code, to add other calls add [OnReady] attributes to methods

        __disposable_DoThing = DoThing();
        DoThing2();
        UpdateDoThing3();
    }
}
");
    }

}

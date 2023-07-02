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
    private void DontDoThing()
    {{
    }}

    [OnReady]
    [AutoDispose]
    private IDisposable? DoThing2()
    {{
        return new List<string>().Select(x => x).GetEnumerator(); 
    }}

    [OnReady]
    [AutoDispose(Accessibility = Accessibility.Internal)]
    private IDisposable? DoThing3()
    {{
        return new List<string>().Select(x => x).GetEnumerator(); 
    }}

    [AutoDispose(Accessibility.Protected)]
    private IDisposable? MyCall(bool foo, string? message)
    {{
        return null;
    }}
}}
";

    private const string MyNodeSourceWithExplicitAttr = $@"
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

    [OnExitTree]
    private void DoThingOnExit()
    {{
    }}

    [OnReady]
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
    public void GeneratesDisposableMembers()
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
using System.Runtime.CompilerServices;
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
        DisposeMyCall();
        DisposeDoThing3();
        DisposeDoThing2();
        __Dispose_DoThing();
    }

    private IDisposable? __disposable_DoThing;

    private void __Dispose_DoThing()
    {
        __disposable_DoThing?.Dispose();
        __disposable_DoThing = null;
    }

    private IDisposable? __disposable_DoThing2;

    public void UpdateDoThing2()
    {
        DisposeDoThing2();
        __disposable_DoThing2 = DoThing2();
    }

    private void DisposeDoThing2()
    {
        __disposable_DoThing2?.Dispose();
        __disposable_DoThing2 = null;
    }

    private IDisposable? __disposable_DoThing3;

    internal void UpdateDoThing3()
    {
        DisposeDoThing3();
        __disposable_DoThing3 = DoThing3();
    }

    private void DisposeDoThing3()
    {
        __disposable_DoThing3?.Dispose();
        __disposable_DoThing3 = null;
    }

    private IDisposable? __disposable_MyCall;

    protected void UpdateMyCall(bool foo, string message)
    {
        DisposeMyCall();
        __disposable_MyCall = MyCall(foo, message);
    }

    private void DisposeMyCall()
    {
        __disposable_MyCall?.Dispose();
        __disposable_MyCall = null;
    }
}
");
    }

    [Fact]
    public void GeneratesExplicitAndDisposableMembers()
    {
        SyntaxTree syntaxIdComponent = CSharpSyntaxTree.ParseText(MyNodeSourceWithExplicitAttr);

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
using System.Runtime.CompilerServices;
using Godot;

#nullable enable

public partial class MyNode
{
    public override void _ExitTree()
    {
        // Generated code, to add other calls add [OnExitTree] attributes to methods

        DoThingOnExit();
        __DisposeOnExitTree();
    }

    private void __DisposeOnExitTree()
    {
        __Dispose_DoThing2();
        __Dispose_DoThing();
    }

    private IDisposable? __disposable_DoThing;

    private void __Dispose_DoThing()
    {
        __disposable_DoThing?.Dispose();
        __disposable_DoThing = null;
    }

    private IDisposable? __disposable_DoThing2;

    private void __Dispose_DoThing2()
    {
        __disposable_DoThing2?.Dispose();
        __disposable_DoThing2 = null;
    }
}
");
    }
}

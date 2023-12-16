using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace GodotHat.SourceGenerators.Test;

public class ScriptMethodsGeneratorTest
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

    public void DoThing2()
    {{
    }}
    
    public void DoThing3(string arg)
    {{
    }}

    public void DoThing4(string[] args)
    {{
    }}

    [GodotIgnore]
    public void DoThingIgnored()
    {{
    }}
}}
";

    public ScriptMethodsGeneratorTest(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        GeneratorTestUtil.ForceAssembliesToBeLoadedFoo();
    }

    [Fact]
    public void GeneratesMembers()
    {
        SyntaxTree syntaxIdComponent = CSharpSyntaxTree.ParseText(MyNodeSource);

        (Compilation? outputCompilation, var diagnostics) =
            GeneratorTestUtil.RunGeneratorCompilation(new ScriptMethodsGenerator(), syntaxIdComponent);
        diagnostics.Should().BeEmpty();

        string? output = outputCompilation.SyntaxTrees
            .Single(tree => tree.FilePath.EndsWith("Test.Node.MyNode_ScriptMethods.generated.cs"))
            ?.ToString().ReplaceLineEndings();

        output.Should()
            .Be(
                @"// Generated code via GodotHat.SourceGenerators.ScriptMethodsGenerator
namespace Test.Node;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot.NativeInterop;
using Godot;

#nullable enable

file static class MethodInfos {
    private static readonly global::Godot.Bridge.MethodInfo _ExitTree = new(
        name: MyNode.MethodName._ExitTree,
        returnVal: new(
            type: global::Godot.Variant.Type.Nil,
            name: new global::Godot.StringName(),
            hint: global::Godot.PropertyHint.None,
            hintString: """",
            usage: global::Godot.PropertyUsageFlags.Storage | global::Godot.PropertyUsageFlags.Editor,
            exported: false),
        flags: global::Godot.MethodFlags.Normal,
        arguments: new() {},
        defaultArguments: null
    );

    private static readonly global::Godot.Bridge.MethodInfo _Ready = new(
        name: MyNode.MethodName._Ready,
        returnVal: new(
            type: global::Godot.Variant.Type.Nil,
            name: new global::Godot.StringName(),
            hint: global::Godot.PropertyHint.None,
            hintString: """",
            usage: global::Godot.PropertyUsageFlags.Storage | global::Godot.PropertyUsageFlags.Editor,
            exported: false),
        flags: global::Godot.MethodFlags.Normal,
        arguments: new() {},
        defaultArguments: null
    );

    private static readonly global::Godot.Bridge.MethodInfo DoThing2 = new(
        name: MyNode.MethodName.DoThing2,
        returnVal: new(
            type: global::Godot.Variant.Type.Nil,
            name: new global::Godot.StringName(),
            hint: global::Godot.PropertyHint.None,
            hintString: """",
            usage: global::Godot.PropertyUsageFlags.Storage | global::Godot.PropertyUsageFlags.Editor,
            exported: false),
        flags: global::Godot.MethodFlags.Normal,
        arguments: new() {},
        defaultArguments: null
    );

    private static readonly global::Godot.Bridge.MethodInfo DoThing3 = new(
        name: MyNode.MethodName.DoThing3,
        returnVal: new(
            type: global::Godot.Variant.Type.Nil,
            name: new global::Godot.StringName(),
            hint: global::Godot.PropertyHint.None,
            hintString: """",
            usage: global::Godot.PropertyUsageFlags.Storage | global::Godot.PropertyUsageFlags.Editor,
            exported: false),
        flags: global::Godot.MethodFlags.Normal,
        arguments: new() {
                new (
                    type: global::Godot.Variant.Type.String,
                    name: new global::Godot.StringName(""arg""),
                    hint: global::Godot.PropertyHint.None,
                    hintString: """",
                    usage: global::Godot.PropertyUsageFlags.Storage | global::Godot.PropertyUsageFlags.Editor,
                    exported: false),
        },
        defaultArguments: null
    );

    private static readonly global::Godot.Bridge.MethodInfo DoThing4 = new(
        name: MyNode.MethodName.DoThing4,
        returnVal: new(
            type: global::Godot.Variant.Type.Nil,
            name: new global::Godot.StringName(),
            hint: global::Godot.PropertyHint.None,
            hintString: """",
            usage: global::Godot.PropertyUsageFlags.Storage | global::Godot.PropertyUsageFlags.Editor,
            exported: false),
        flags: global::Godot.MethodFlags.Normal,
        arguments: new() {
                new (
                    type: global::Godot.Variant.Type.PackedStringArray,
                    name: new global::Godot.StringName(""args""),
                    hint: global::Godot.PropertyHint.None,
                    hintString: """",
                    usage: global::Godot.PropertyUsageFlags.Storage | global::Godot.PropertyUsageFlags.Editor,
                    exported: false),
        },
        defaultArguments: null
    );


    public static readonly global::System.Collections.Generic.List<global::Godot.Bridge.MethodInfo> GodotMethodList = new() {
        MethodInfos._ExitTree,
        MethodInfos._Ready,
        MethodInfos.DoThing2,
        MethodInfos.DoThing3,
        MethodInfos.DoThing4,
};
}

public partial class MyNode
{
    #pragma warning disable CS0109 // Disable warning about redundant 'new' keyword
    public new class MethodName : global::Godot.Node.MethodName
    {
        public new static readonly global::Godot.StringName _ExitTree = ""_ExitTree"";
        public new static readonly global::Godot.StringName _Ready = ""_Ready"";
        public new static readonly global::Godot.StringName DoThing2 = ""DoThing2"";
        public new static readonly global::Godot.StringName DoThing3 = ""DoThing3"";
        public new static readonly global::Godot.StringName DoThing4 = ""DoThing4"";
    }

    internal new static global::System.Collections.Generic.List<global::Godot.Bridge.MethodInfo> GetGodotMethodList()
    {
        return MethodInfos.GodotMethodList;
    }
    #pragma warning restore CS0109

    protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
    {
        if (method == MethodName._ExitTree && args.Count == 0)
        {
            _ExitTree();
            ret = default;
            return true;
        }
        if (method == MethodName._Ready && args.Count == 0)
        {
            _Ready();
            ret = default;
            return true;
        }
        if (method == MethodName.DoThing2 && args.Count == 0)
        {
            DoThing2();
            ret = default;
            return true;
        }
        if (method == MethodName.DoThing3 && args.Count == 1)
        {
            DoThing3(
                // arg
                global::Godot.NativeInterop.VariantUtils.ConvertTo<global::Godot.Variant.Type.String>(args[0]));
            ret = default;
            return true;
        }
        if (method == MethodName.DoThing4 && args.Count == 1)
        {
            DoThing4(
                // args
                global::Godot.NativeInterop.VariantUtils.ConvertTo<global::Godot.Variant.Type.PackedStringArray>(args[0]));
            ret = default;
            return true;
        }

        return base.InvokeGodotClassMethod(method, args, out ret);
    }

    protected override bool HasGodotClassMethod(in godot_string_name method)
    {
        if (method == MethodName._ExitTree) return true;
        if (method == MethodName._Ready) return true;
        if (method == MethodName.DoThing2) return true;
        if (method == MethodName.DoThing3) return true;
        if (method == MethodName.DoThing4) return true;

        return base.HasGodotClassMethod(method);
    }
}
".ReplaceLineEndings());
    }

}

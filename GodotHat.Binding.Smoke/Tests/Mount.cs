namespace GodotHat.Binding.Smoke.Tests;

using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

// Helpers for building bound scenes in tests.
internal static class Mount
{
    public static SceneTree MainTree => (SceneTree)Engine.GetMainLoop();

    public static async Task Frames(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            await MainTree.ToSignal(MainTree, SceneTree.SignalName.ProcessFrame);
        }
    }

    public static async Task<T> AddToTree<T>(T node)
        where T : Node
    {
        MainTree.Root.AddChild(node);
        await Frames();
        return node;
    }

    // Removes and frees a node now, then lets queued frees (eg pooled item rows) happen, so no nodes are orphaned.
    public static async Task Unmount(Node node)
    {
        if (node.GetParent() is { } parent)
        {
            parent.RemoveChild(node);
        }

        node.Free();
        await Frames();
    }

    public static T Bind<T>(this T node, string key, BindingDef definition)
        where T : Node
    {
        Godot.Collections.Dictionary bindings = node.HasMeta(BindingRootBase.BindingsMetaKey)
            ? node.GetMeta(BindingRootBase.BindingsMetaKey).AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        bindings[key] = definition;
        node.SetMeta(BindingRootBase.BindingsMetaKey, bindings);
        return node;
    }

    public static T With<T>(this T node, params Node[] children)
        where T : Node
    {
        foreach (Node child in children)
        {
            node.AddChild(child);
        }

        return node;
    }

    public static BindingDef Def(
        string path,
        BindingMode mode = BindingMode.OneWay,
        string converter = "",
        string argument = "",
        BindingKind kind = BindingKind.Property) =>
        new() { Path = path, Mode = mode, Converter = converter, ConverterArg = argument, Kind = kind };

    public static BindingDef Command(string path) => Def(path, kind: BindingKind.Command);
}

// Collects binding errors during a test.
internal sealed class ErrorLog : System.IDisposable
{
    public ErrorLog() => GodotBinding.ErrorReported += this.OnError;

    public List<BindingError> Errors { get; } = new();

    public void Dispose() => GodotBinding.ErrorReported -= this.OnError;

    private void OnError(BindingError error)
    {
        lock (this.Errors)
        {
            this.Errors.Add(error);
        }
    }
}

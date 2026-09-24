#if TOOLS
namespace GodotHat.Binding.Editor;

using System.Collections.Generic;
using Godot;

// Reads and edits the bindings on nodes in the scene being edited, through the editor's undo history.
internal static class EditorBindings
{
    private static readonly StringName Meta = BindingRootBase.BindingsMetaKey;

    public static IReadOnlyDictionary<string, BindingDefBase> Read(Node node) => BindingValidator.ReadBindings(node);

    public static bool TryGet(Node node, string key, out BindingDefBase? definition) => Read(node).TryGetValue(key, out definition);

    // A copy of a binding to change, as scenes share their resources.
    public static BindingDef Copy(BindingDefBase? definition) =>
        definition is BindingDef existing ? (BindingDef)existing.Duplicate() : new BindingDef();

    // Sets or, with null, removes a binding, as an undoable action.
    public static void Set(Node node, string key, BindingDef? definition, string action)
    {
        Variant old = node.HasMeta(Meta) ? node.GetMeta(Meta) : default;
        var next = new Godot.Collections.Dictionary<StringName, BindingDef>();
        foreach ((string existingKey, BindingDefBase existing) in Read(node))
        {
            if (existingKey != key && existing is BindingDef binding)
            {
                next[existingKey] = binding;
            }
        }

        if (definition is not null)
        {
            next[key] = definition;
        }

        EditorUndoRedoManager undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction(action, UndoRedo.MergeMode.Disable, node);
        undo.AddDoMethod(node, Node.MethodName.SetMeta, Meta, next.Count > 0 ? next : default(Variant));
        undo.AddDoMethod(node, GodotObject.MethodName.NotifyPropertyListChanged);
        undo.AddUndoMethod(node, Node.MethodName.SetMeta, Meta, old);
        undo.AddUndoMethod(node, GodotObject.MethodName.NotifyPropertyListChanged);
        undo.CommitAction();
    }

    public static bool IsInEditedScene(Node node) =>
        EditorInterface.Singleton.GetEditedSceneRoot() is { } root && (node == root || root.IsAncestorOf(node));

    public static string Describe(BindingDefBase definition)
    {
        string converter = definition.Converter.IsEmpty ? "" : $" | {definition.Converter}";
        string mode = definition.Mode == BindingMode.OneWay ? "" : $" ({definition.Mode})";
        return $"{(definition.Path.Length == 0 ? "<context>" : definition.Path)}{converter}{mode}";
    }

    public static Texture2D? Icon(string name)
    {
        Theme theme = EditorInterface.Singleton.GetEditorTheme();
        return theme.HasIcon(name, "EditorIcons") ? theme.GetIcon(name, "EditorIcons") : null;
    }
}
#endif

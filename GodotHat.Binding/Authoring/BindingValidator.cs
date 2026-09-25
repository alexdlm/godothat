using Godot;

namespace GodotHat.Binding;

/// <summary>How a binding checked out.</summary>
public enum BindingStatus
{
    /// <summary>The binding resolves and suits its target.</summary>
    Ok,

    /// <summary>The binding can't be checked, as its data context's type isn't declared.</summary>
    Unchecked,

    /// <summary>The binding works, but probably not as intended.</summary>
    Warning,

    /// <summary>The binding will fail.</summary>
    Error,
}

/// <summary>A binding found in a scene, and how it checked out.</summary>
/// <param name="Scene">The scene's resource path, if known.</param>
/// <param name="NodePath">The node's path from the scene root.</param>
/// <param name="Key">The binding's key: a property, signal, method, or <c>@context</c>, <c>@items</c> or <c>@selected</c>.</param>
/// <param name="Path">The binding's source path.</param>
/// <param name="Status">How it checked out.</param>
/// <param name="Message">What's wrong, if anything.</param>
public sealed record BindingReport(string Scene, string NodePath, string Key, string Path, BindingStatus Status, string Message)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{this.Status}: {this.Scene}:{this.NodePath} {this.Key} = '{this.Path}'{(this.Message.Length > 0 ? $": {this.Message}" : "")}";
}

/// <summary>
/// Checks the bindings in scenes without running them: paths against the view model types that binding roots declare
/// with <c>ViewModelType</c>, converters, and targets against Godot's property, signal and method lists and the target
/// rules. Item templates are checked against their collection's item type.
/// </summary>
/// <example>
/// A test that fails on any broken binding in the project, using GodotHat.Binding.Testing:
/// <code>
/// [TestCase]
/// public void BindingsAreValid() => BindingValidation.AssertProjectValid();
/// </code>
/// Or in CI, after importing the project:
/// <c>godot --headless --path . --script res://addons/godothat_binding/ValidateBindings.cs</c>.
/// </example>
public static class BindingValidator
{
    private static readonly StringName BindingsMeta = BindingRootBase.BindingsMetaKey;

    /// <summary>Checks every scene under <paramref name="directory"/>, other than addons.</summary>
    /// <param name="directory">The directory to search, eg <c>res://ui</c>.</param>
    /// <param name="rules">The target rules; the project's by default.</param>
    public static IReadOnlyList<BindingReport> ValidateProject(string directory = "res://", TargetRules? rules = null)
    {
        rules ??= GodotTargets.ProjectRules();
        var reports = new List<BindingReport>();
        foreach (string scenePath in FindScenes(directory))
        {
            reports.AddRange(ValidateScene(scenePath, rules));
        }

        return reports;
    }

    /// <summary>
    /// Checks the scene at <paramref name="scenePath"/>, instancing it without adding it to the tree. A scene that can't
    /// be loaded is reported as an error.
    /// </summary>
    public static IReadOnlyList<BindingReport> ValidateScene(string scenePath, TargetRules? rules = null)
    {
        if (ResourceLoader.Load<PackedScene>(scenePath) is not { } scene ||
            scene.Instantiate(PackedScene.GenEditState.Disabled) is not { } root)
        {
            return [new BindingReport(scenePath, ".", "", "", BindingStatus.Error, "The scene can't be loaded.")];
        }

        try
        {
            return Validate(root, scenePath, rules ?? GodotTargets.ProjectRules(), contextType: null);
        }
        finally
        {
            root.Free();
        }
    }

    /// <summary>Checks the bindings in the tree under <paramref name="root"/>, eg the scene being edited.</summary>
    /// <param name="root">The scene root.</param>
    /// <param name="scenePath">The scene's path, for reports.</param>
    /// <param name="rules">The target rules; the project's by default.</param>
    /// <param name="contextType">The data context's type at the root, if not declared by a binding root.</param>
    public static IReadOnlyList<BindingReport> Validate(Node root, string scenePath = "", TargetRules? rules = null, Type? contextType = null)
    {
        var reports = new List<BindingReport>();
        new Walker(root, scenePath, rules ?? GodotTargets.ProjectRules(), reports).Visit(root, contextType, null);
        return reports;
    }

    /// <summary>
    /// The data context type at <paramref name="node"/>, from the binding roots and context bindings above it, or null if
    /// it isn't declared. For the editor's path picker.
    /// </summary>
    /// <param name="node">The node whose bindings resolve against the context.</param>
    /// <param name="children">The context for the node's children, which its own <c>@context</c> binding sets.</param>
    public static Type? ContextTypeAt(Node node, bool children = false)
    {
        var chain = new List<Node>();
        for (Node? current = children ? node : node.GetParent(); current is not null; current = current.GetParent())
        {
            chain.Add(current);
        }

        chain.Reverse();
        Type? type = null;
        foreach (Node current in chain)
        {
            if (current is BindingRootBase root && root.DeclaredViewModelType is { } declared)
            {
                type = declared;
            }

            if (type is not null && ReadBindings(current).TryGetValue(BindingRootBase.ContextKey, out BindingDefBase? context))
            {
                type = BindingPath.TryCompile(type, context.Path, out BindingPath? path, out _) ? (path.Leaf?.ValueType ?? type) : null;
            }
        }

        return type;
    }

    /// <summary>The bindings stored on <paramref name="node"/>, by key.</summary>
    public static IReadOnlyDictionary<string, BindingDefBase> ReadBindings(Node node)
    {
        var bindings = new Dictionary<string, BindingDefBase>();
        if (node.HasMeta(BindingsMeta) && GodotData.ReadMetaDictionary(node, BindingsMeta) is { } entries)
        {
            foreach ((string key, GodotObject? value) in entries)
            {
                if (value is BindingDefBase definition)
                {
                    bindings[key] = definition;
                }
            }
        }

        return bindings;
    }

    private static IEnumerable<string> FindScenes(string directory)
    {
        using DirAccess? dir = DirAccess.Open(directory);
        if (dir is null)
        {
            yield break;
        }

        foreach (string sub in dir.GetDirectories())
        {
            if (sub is "addons" or ".godot" || sub.StartsWith('.'))
            {
                continue;
            }

            foreach (string scene in FindScenes(directory.PathJoin(sub)))
            {
                yield return scene;
            }
        }

        foreach (string file in dir.GetFiles())
        {
            if (file.EndsWith(".tscn", StringComparison.Ordinal) || file.EndsWith(".scn", StringComparison.Ordinal))
            {
                yield return directory.PathJoin(file);
            }
        }
    }

    private sealed class Walker(Node sceneRoot, string scenePath, TargetRules rules, List<BindingReport> reports)
    {
        public void Visit(Node node, Type? contextType, Type? parentContextType)
        {
            if (node is BindingRootBase root && root.DeclaredViewModelType is { } declared)
            {
                parentContextType = contextType;
                contextType = declared;
            }
            else if (node is BindingRootBase { ViewModelType.Length: > 0 } undeclared)
            {
                this.Report(node, "ViewModelType", undeclared.ViewModelType, BindingStatus.Error, $"'{undeclared.ViewModelType}' is not a registered [ViewModel].");
            }

            IReadOnlyDictionary<string, BindingDefBase> bindings = ReadBindings(node);
            Type? childContext = contextType;
            Type? itemType = null;
            foreach ((string key, BindingDefBase definition) in bindings.OrderBy(b => b.Key == BindingRootBase.SelectedKey))
            {
                Type? source = definition.Source == BindingSourceKind.ParentContext ? parentContextType : contextType;
                switch (key)
                {
                    case BindingRootBase.ContextKey:
                        childContext = this.CheckContext(node, key, definition, source);
                        break;
                    case BindingRootBase.ItemsKey:
                        itemType = this.CheckItems(node, key, definition, source);
                        break;
                    case BindingRootBase.SelectedKey:
                        this.CheckSelected(node, key, definition, source, itemType);
                        break;
                    default:
                        this.CheckBinding(node, key, definition, source);
                        break;
                }
            }

            if (node is ContentPresenterBase)
            {
                return;
            }

            foreach (Node child in node.GetChildren())
            {
                this.Visit(child, childContext, childContext == contextType ? parentContextType : contextType);
            }
        }

        private Type? CheckContext(Node node, string key, BindingDefBase definition, Type? contextType)
        {
            if (!this.TryResolve(node, key, definition, contextType, out BindingPath? path))
            {
                return null;
            }

            MemberAccessor? leaf = path.Leaf;
            if (leaf is not null && (leaf is not ValueMember || leaf.ValueType.IsValueType))
            {
                this.Report(node, key, definition.Path, BindingStatus.Error, $"{leaf.Name} can't be a data context; it must be a view model or other object.");
                return null;
            }

            this.Report(node, key, definition.Path, BindingStatus.Ok, "");
            return leaf?.ValueType ?? contextType;
        }

        private Type? CheckItems(Node node, string key, BindingDefBase definition, Type? contextType)
        {
            if (!this.TryResolve(node, key, definition, contextType, out BindingPath? path))
            {
                return null;
            }

            if (path.Leaf is not { Kind: MemberKind.Items } items)
            {
                this.Report(node, key, definition.Path, BindingStatus.Error, $"{path.Leaf?.Name ?? "The context"} is not a collection.");
                return null;
            }

            bool listControl = node is ItemList or OptionButton or Tree;
            if (listControl)
            {
                this.CheckPath(node, "ItemText", definition.ItemText, items.ValueType);
                if (!string.IsNullOrEmpty(definition.ItemIcon))
                {
                    this.CheckPath(node, "ItemIcon", definition.ItemIcon, items.ValueType);
                }
            }
            else if (definition.Template is null)
            {
                this.Report(node, key, definition.Path, BindingStatus.Error, $"'{key}' on {node.GetClass()} needs a Template scene.");
                return items.ValueType;
            }
            else
            {
                Node template = definition.Template.Instantiate(PackedScene.GenEditState.Disabled);
                try
                {
                    new Walker(template, definition.Template.ResourcePath, rules, reports).Visit(template, items.ValueType, contextType);
                }
                finally
                {
                    template.Free();
                }
            }

            this.Report(node, key, definition.Path, BindingStatus.Ok, "");
            return items.ValueType;
        }

        private void CheckSelected(Node node, string key, BindingDefBase definition, Type? contextType, Type? itemType)
        {
            if (node is not (ItemList or OptionButton or Tree))
            {
                this.Report(node, key, definition.Path, BindingStatus.Error, $"'{key}' only applies to an ItemList, OptionButton or Tree with an '{BindingRootBase.ItemsKey}' binding.");
                return;
            }

            if (this.TryResolve(node, key, definition, contextType, out BindingPath? path))
            {
                bool fits = itemType is null || path.Leaf is null || path.Leaf.ValueType.IsAssignableFrom(itemType) || itemType.IsAssignableFrom(path.Leaf.ValueType);
                this.Report(
                    node,
                    key,
                    definition.Path,
                    fits ? BindingStatus.Ok : BindingStatus.Error,
                    fits ? "" : $"{path.Leaf!.ValueType.Name} can't hold the items, which are {itemType!.Name}.");
            }
        }

        private void CheckBinding(Node node, string key, BindingDefBase definition, Type? contextType)
        {
            switch (definition.Kind)
            {
                case BindingKind.Command:
                    if (!node.HasSignal(key))
                    {
                        this.Report(node, key, definition.Path, BindingStatus.Error, $"{node.GetClass()} has no signal '{key}'.");
                    }
                    else if (this.TryResolve(node, key, definition, contextType, out BindingPath? command))
                    {
                        this.ReportKind(node, key, definition, command.Leaf?.Kind == MemberKind.Command, "is not a command");
                    }

                    return;
                case BindingKind.Event:
                    if (!node.HasMethod(key))
                    {
                        this.Report(node, key, definition.Path, BindingStatus.Error, $"{node.GetClass()} has no method '{key}'.");
                    }
                    else if (this.TryResolve(node, key, definition, contextType, out BindingPath? observable))
                    {
                        this.ReportKind(node, key, definition, observable.Leaf is null or ValueMember, "is not an observable");
                    }

                    return;
                case BindingKind.Property:
                    this.CheckProperty(node, key, definition, contextType);
                    return;
                default:
                    this.Report(node, key, definition.Path, BindingStatus.Error, $"Kind {definition.Kind} only applies to '{BindingRootBase.ContextKey}' or '{BindingRootBase.ItemsKey}'.");
                    return;
            }
        }

        private void CheckProperty(Node node, string key, BindingDefBase definition, Type? contextType)
        {
            if (!GodotTargets.TryGetProperty(node, key, out TargetPropertyInfo property))
            {
                this.Report(node, key, definition.Path, BindingStatus.Error, $"{node.GetClass()} has no property '{key}'.");
                return;
            }

            if (!rules.IsBindable(property, out string reason))
            {
                this.Report(node, key, definition.Path, BindingStatus.Warning, $"'{key}' isn't offered as a target because {reason}.");
                return;
            }

            if (!this.TryResolve(node, key, definition, contextType, out BindingPath? path))
            {
                return;
            }

            MemberAccessor? leaf = path.Leaf;
            if (leaf?.Kind == MemberKind.Command)
            {
                this.Report(node, key, definition.Path, BindingStatus.Error, $"{leaf.Name} is a command; bind it to a signal instead.");
                return;
            }

            string converter = definition.Converter.ToString();
            if (converter.Length > 0 && !GodotBinding.Converters.TryGet(converter, out _))
            {
                this.Report(node, key, definition.Path, BindingStatus.Error, $"Unknown converter '{converter}'.");
                return;
            }

            if (definition.Mode == BindingMode.TwoWay && leaf is ValueMember { CanWrite: false })
            {
                this.Report(node, key, definition.Path, BindingStatus.Warning, $"{leaf.Name} is read-only, so the binding is one-way.");
                return;
            }

            if (leaf is null || converter.Length > 0 && ConvertedType(converter) is not { } _)
            {
                this.Report(node, key, definition.Path, BindingStatus.Ok, "");
                return;
            }

            Type valueType = converter.Length > 0 ? ConvertedType(converter)! : leaf.ValueType;
            bool isCollection = converter.Length == 0 && leaf.Kind == MemberKind.Items;
            PathCompatibility compatibility = PathSuggester.Check(valueType, isCollection, GodotTargets.TypeOf(property), out IReadOnlyList<string> suggested);
            switch (compatibility)
            {
                case PathCompatibility.Compatible:
                    this.Report(node, key, definition.Path, BindingStatus.Ok, "");
                    break;
                case PathCompatibility.NeedsConverter:
                    this.Report(node, key, definition.Path, BindingStatus.Error, $"'{key}' can't take {valueType.Name} directly; add a converter such as {string.Join(" or ", suggested)}.");
                    break;
                default:
                    this.Report(node, key, definition.Path, BindingStatus.Error, $"'{key}' can't take {valueType.Name}.");
                    break;
            }
        }

        private void CheckPath(Node node, string key, string text, Type contextType)
        {
            if (BindingPath.TryCompile(contextType, text, out _, out string? error))
            {
                this.Report(node, key, text, BindingStatus.Ok, "");
            }
            else
            {
                this.Report(node, key, text, BindingStatus.Error, error);
            }
        }

        private bool TryResolve(Node node, string key, BindingDefBase definition, Type? contextType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BindingPath? path)
        {
            path = null;
            if (contextType is null)
            {
                this.Report(node, key, definition.Path, BindingStatus.Unchecked, "The data context's type isn't declared; set ViewModelType on the binding root.");
                return false;
            }

            if (!BindingPath.TryCompile(contextType, definition.Path, out path, out string? error))
            {
                this.Report(node, key, definition.Path, BindingStatus.Error, error);
                return false;
            }

            return true;
        }

        private void ReportKind(Node node, string key, BindingDefBase definition, bool fits, string problem) =>
            this.Report(node, key, definition.Path, fits ? BindingStatus.Ok : BindingStatus.Error, fits ? "" : $"'{definition.Path}' {problem}.");

        private void Report(Node node, string key, string path, BindingStatus status, string message) =>
            reports.Add(new BindingReport(scenePath, sceneRoot.GetPathTo(node).ToString(), key, path, status, message));

        // The value type the built-in converters produce; null for custom converters, whose output isn't known.
        private static Type? ConvertedType(string converter) => converter switch
        {
            BuiltInConverters.NotId or BuiltInConverters.InvertId or BuiltInConverters.NotNullId or BuiltInConverters.IsNullId or BuiltInConverters.EqualsId or BuiltInConverters.AnyId => typeof(bool),
            BuiltInConverters.ToStringId or BuiltInConverters.FormatId or BuiltInConverters.EnumNameId => typeof(string),
            BuiltInConverters.CountId => typeof(int),
            _ => null,
        };
    }
}

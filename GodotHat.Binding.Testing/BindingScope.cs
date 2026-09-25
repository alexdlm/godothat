using Godot;

namespace GodotHat.Binding.Testing;

/// <summary>
/// Finds Controls by what they bind to, so tests survive layout changes and renames. The scope is part of a mounted
/// scene: all of it for <see cref="BindingHarness"/>, one row for <see cref="BoundItems.Row"/>, or what a presenter
/// shows for <see cref="BoundPresenter"/>.
/// </summary>
public abstract class BindingScope
{
    private protected BindingScope()
    {
    }

    /// <summary>The node the scope searches, along with everything below it.</summary>
    public abstract Node Node { get; }

    /// <summary>Finds the one Control of type <typeparamref name="T"/> with a binding to <paramref name="path"/>.</summary>
    /// <param name="path">The binding's source path, eg <c>PlayerName</c>, <c>Save</c> or <c>Selected.Name</c>.</param>
    /// <param name="target">
    /// The bound property, signal or method, eg <c>text</c>, when Controls of the same type bind the path differently.
    /// </param>
    /// <exception cref="BindingAssertionException">No Control, or more than one, matches.</exception>
    public BoundControl<T> Bound<T>(string path, string? target = null)
        where T : Control
    {
        List<(Node Node, string Key)> bindings = this.FindBindings(path, target);
        List<T> matches = bindings.Select(b => b.Node).OfType<T>().Distinct().ToList();
        if (matches.Count == 1)
        {
            return new BoundControl<T>(matches[0], this.Describe(matches[0], path));
        }

        string what = target is null ? $"'{path}'" : $"'{path}' to {target}";
        if (matches.Count == 0)
        {
            string others = bindings.Count == 0
                ? ""
                : $" Other nodes bind it: {string.Join(", ", bindings.Select(b => $"{this.Describe(b.Node)} ({b.Key})"))}.";
            throw new BindingAssertionException($"No {typeof(T).Name} in {this.Describe(this.Node)} binds {what}.{others}");
        }

        throw new BindingAssertionException(
            $"{matches.Count} {typeof(T).Name}s in {this.Describe(this.Node)} bind {what}: " +
            $"{string.Join(", ", matches.Select(m => this.Describe(m)))}. Pass a target, or search one row or presenter.");
    }

    /// <summary>Finds the one Control with a binding to <paramref name="path"/>.</summary>
    /// <inheritdoc cref="Bound{T}(string, string?)"/>
    public BoundControl<Control> Bound(string path, string? target = null) => this.Bound<Control>(path, target);

    /// <summary>Finds the Control whose <c>@items</c> binding shows the collection at <paramref name="path"/>.</summary>
    /// <exception cref="BindingAssertionException">No Control, or more than one, matches.</exception>
    public BoundItems Items(string path)
    {
        Control control = this.Single<Control>(
            this.FindBindings(path, BindingRootBase.ItemsKey).Select(b => b.Node),
            $"'{BindingRootBase.ItemsKey}' binding to '{path}'");
        return new BoundItems(control, this.Describe(control, path));
    }

    /// <summary>
    /// Finds the presenter whose <c>@context</c> binding is <paramref name="path"/>, or the only presenter when no path is
    /// given.
    /// </summary>
    /// <exception cref="BindingAssertionException">No presenter, or more than one, matches.</exception>
    public BoundPresenter Presenter(string? path = null)
    {
        IEnumerable<Node> candidates = path is null
            ? Descendants(this.Node)
            : this.FindBindings(path, BindingRootBase.ContextKey).Select(b => b.Node);
        ContentPresenterBase presenter = this.Single<ContentPresenterBase>(
            candidates,
            path is null ? "ContentPresenter" : $"ContentPresenter bound to '{path}'");
        return new BoundPresenter(presenter, this.Describe(presenter, path));
    }

    /// <summary>
    /// Finds a Control by its node path from <see cref="Node"/>, or its unique name such as <c>%Title</c>, for Controls
    /// with no binding to find them by.
    /// </summary>
    /// <exception cref="BindingAssertionException">There's no such node, or it isn't a <typeparamref name="T"/>.</exception>
    public BoundControl<T> Find<T>(string nodePath)
        where T : Control
    {
        Node? node = this.Node.GetNodeOrNull(nodePath);
        return node is T control
            ? new BoundControl<T>(control, this.Describe(control))
            : throw new BindingAssertionException(
                node is null
                    ? $"{this.Describe(this.Node)} has no node '{nodePath}'."
                    : $"{this.Describe(node)} is not a {typeof(T).Name}.");
    }

    private protected string Describe(Node node, string? path = null)
    {
        string where = node == this.Node || !this.Node.IsAncestorOf(node) ? node.Name : this.Node.GetPathTo(node);
        return path is null ? $"{node.GetType().Name} '{where}'" : $"{node.GetType().Name} '{where}' ({path})";
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
        {
            foreach (Node node in Descendants(child))
            {
                yield return node;
            }
        }
    }

    private List<(Node Node, string Key)> FindBindings(string path, string? key)
    {
        var matches = new List<(Node, string)>();
        foreach (Node node in Descendants(this.Node))
        {
            foreach ((string bindingKey, BindingDefBase definition) in BindingValidator.ReadBindings(node))
            {
                if (definition.Path == path && (key is null || bindingKey == key))
                {
                    matches.Add((node, bindingKey));
                }
            }
        }

        return matches;
    }

    private T Single<T>(IEnumerable<Node> candidates, string what)
        where T : Node
    {
        List<T> matches = candidates.OfType<T>().Distinct().ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new BindingAssertionException($"{this.Describe(this.Node)} has no {what}."),
            _ => throw new BindingAssertionException(
                $"{this.Describe(this.Node)} has {matches.Count} of {what}: {string.Join(", ", matches.Select(m => this.Describe(m)))}."),
        };
    }
}

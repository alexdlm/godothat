using Godot;

namespace GodotHat.Binding.Testing;

/// <summary>
/// A Control showing a collection through an <c>@items</c> binding: a container with a row per item, or an ItemList,
/// OptionButton or Tree.
/// </summary>
public sealed class BoundItems
{
    private readonly BoundControl<Control> control;
    private readonly string description;

    internal BoundItems(Control control, string description)
    {
        this.control = new BoundControl<Control>(control, description);
        this.description = description;
    }

    /// <summary>The Control.</summary>
    public Control Control => this.control.Control;

    /// <summary>How many items it shows.</summary>
    public int Count => this.Control is ItemList or OptionButton or Tree ? this.control.ItemTexts().Count : this.Rows().Count;

    /// <summary>The text of each item of an ItemList, OptionButton or Tree.</summary>
    public IReadOnlyList<string> Texts => this.control.ItemTexts();

    /// <summary>The row showing item <paramref name="index"/> in a container, to find the Controls in it.</summary>
    /// <exception cref="BindingAssertionException">It isn't a container, or has no such row.</exception>
    public BindingScope Row(int index)
    {
        IReadOnlyList<Node> rows = this.Rows();
        return index >= 0 && index < rows.Count
            ? new RowScope(rows[index])
            : throw new BindingAssertionException($"{this.description} has {rows.Count} rows, so there's no row {index}.");
    }

    /// <summary>Checks how many items it shows.</summary>
    public BoundItems AssertCount(int expected)
    {
        int count = this.Count;
        return count == expected
            ? this
            : throw new BindingAssertionException($"{this.description} expected {expected} items, but has {count}.");
    }

    /// <summary>Checks the text of each item of an ItemList, OptionButton or Tree.</summary>
    public BoundItems AssertTexts(params string[] expected)
    {
        IReadOnlyList<string> texts = this.Texts;
        return texts.SequenceEqual(expected)
            ? this
            : throw new BindingAssertionException(
                $"{this.description} expected items [{string.Join(", ", expected)}], but has [{string.Join(", ", texts)}].");
    }

    /// <summary>Checks which item of an ItemList, OptionButton or Tree is selected, by index, or -1 for none.</summary>
    public BoundItems AssertSelected(int expected)
    {
        this.control.AssertSelected(expected);
        return this;
    }

    /// <summary>Selects item <paramref name="index"/> of an ItemList, OptionButton or Tree, as a click would.</summary>
    /// <inheritdoc cref="BoundControl{T}.Select(int)"/>
    public Task Select(int index) => this.control.Select(index);

    /// <summary>Selects the item of an ItemList, OptionButton or Tree with the text <paramref name="text"/>.</summary>
    /// <inheritdoc cref="BoundControl{T}.Select(int)"/>
    public Task Select(string text) => this.control.Select(text);

    private IReadOnlyList<Node> Rows()
    {
        for (Node? node = this.Control; node is not null; node = node.GetParent())
        {
            if (node is BindingRootBase root)
            {
                return root.ItemInstances(this.Control)
                       ?? throw new BindingAssertionException($"{this.description} isn't bound to rows; is its binding root bound?");
            }
        }

        throw new BindingAssertionException($"{this.description} isn't inside a binding root.");
    }

    private sealed class RowScope(Node row) : BindingScope
    {
        public override Node Node => row;
    }
}

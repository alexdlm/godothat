using System.Diagnostics.CodeAnalysis;
using Godot;
using R3;

namespace GodotHat.Binding;

// Item sources for list controls that draw their own items: each item becomes an entry whose text (and optionally
// icon) is bound from the item through BindingDef.ItemText and ItemIcon, so view model items update in place.
internal abstract class ListItemsTarget(Control control, BindingDefBase definition, DataContext parentContext)
    : IItemsTarget, IDisposable
{
    private readonly List<Entry> entries = new();
    private string? description;

    public string Description => this.description ??= $"{control.GetPath()}:{BindingRootBase.ItemsKey}";

    public object? SelectedItem
    {
        get
        {
            int index = this.SelectedIndex;
            return index >= 0 && index < this.entries.Count ? this.entries[index].Item.Value : null;
        }
    }

    public abstract string SelectionSignal { get; }

    protected abstract int SelectedIndex { get; }

    public static ListItemsTarget? For(Node node, BindingDefBase definition, DataContext parentContext) => node switch
    {
        ItemList list => new ItemListItemsTarget(list, definition, parentContext),
        OptionButton option => new OptionButtonItemsTarget(option, definition, parentContext),
        Tree tree => new TreeItemsTarget(tree, definition, parentContext),
        _ => null,
    };

    public bool CanAccept<TItem>([NotNullWhen(false)] out string? error)
    {
        error = null;
        return true;
    }

    public void Reset<TItem>(ReadOnlySpan<TItem> items)
    {
        object? selected = this.SelectedItem;
        foreach (Entry entry in this.entries)
        {
            entry.Dispose();
        }

        this.entries.Clear();
        this.ClearItems();
        this.Insert(0, items);
        this.SelectItem(selected);
    }

    public void Insert<TItem>(int index, ReadOnlySpan<TItem> items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var values = new ReactiveProperty<object?>(items[i], ReferenceEqualityComparer.Instance);
            var entry = new Entry(values, DataContext.Create<TItem>(values, parentContext));
            this.entries.Insert(index + i, entry);
            this.InsertItem(index + i);
            this.Bind(entry);
        }

        this.StructureChanged();
    }

    public void Remove(int index, int count)
    {
        for (int i = 0; i < count; i++)
        {
            this.entries[index].Dispose();
            this.entries.RemoveAt(index);
            this.RemoveItem(index);
        }

        this.StructureChanged();
    }

    public void Move(int oldIndex, int newIndex)
    {
        Entry entry = this.entries[oldIndex];
        this.entries.RemoveAt(oldIndex);
        this.entries.Insert(newIndex, entry);
        this.MoveItem(oldIndex, newIndex);
        this.StructureChanged();
    }

    public void Replace<TItem>(int index, TItem item) => this.entries[index].Item.Value = item;

    public void SelectItem(object? item)
    {
        for (int i = 0; i < this.entries.Count; i++)
        {
            if (item is not null && this.entries[i].Item.Value is { } candidate && ItemKeyComparer.Instance.Equals(candidate, item))
            {
                this.Select(i);
                return;
            }
        }

        this.Select(-1);
    }

    public void Dispose()
    {
        foreach (Entry entry in this.entries)
        {
            entry.Dispose();
        }

        this.entries.Clear();
        if (GodotObject.IsInstanceValid(control))
        {
            this.ClearItems();
        }
    }

    protected IReadOnlyList<Entry> Entries => this.entries;

    protected abstract void InsertItem(int index);

    protected abstract void RemoveItem(int index);

    protected abstract void MoveItem(int oldIndex, int newIndex);

    protected abstract void SetItemText(int index, string text);

    protected abstract void SetItemIcon(int index, Texture2D? icon);

    protected abstract void ClearItems();

    protected abstract void Select(int index);

    // Controls that can't insert or move items rebuild them from the entries here.
    protected virtual void StructureChanged()
    {
    }

    private void Bind(Entry entry)
    {
        BindingEngine engine = GodotBinding.Engine;
        var text = new BindingSpec
        {
            Path = definition.ItemText,
            Converter = definition.Converter.IsEmpty ? BuiltInConverters.ToStringId : definition.Converter.ToString(),
            ConverterArgument = string.IsNullOrEmpty(definition.ConverterArg) ? null : definition.ConverterArg,
        };
        entry.Bindings.Add(engine.BindProperty(entry.Context, text, new EntryTarget(this, entry, isIcon: false)));
        if (!string.IsNullOrEmpty(definition.ItemIcon))
        {
            entry.Bindings.Add(engine.BindProperty(entry.Context, new BindingSpec { Path = definition.ItemIcon }, new EntryTarget(this, entry, isIcon: true)));
        }
    }

    protected sealed class Entry(ReactiveProperty<object?> item, DataContext context) : IDisposable
    {
        public ReactiveProperty<object?> Item { get; } = item;
        public DataContext Context { get; } = context;
        public CompositeDisposable Bindings { get; } = new();
        public string Text { get; set; } = "";
        public Texture2D? Icon { get; set; }

        public void Dispose()
        {
            this.Bindings.Dispose();
            this.Item.Dispose();
        }
    }

    // An entry's text or icon as a binding target.
    private sealed class EntryTarget(ListItemsTarget list, Entry entry, bool isIcon) : IBindingTarget
    {
        public string Description => $"{list.Description} {(isIcon ? "icon" : "text")}";

        public bool CanAccept<T>([NotNullWhen(false)] out string? error)
        {
            bool accepted = isIcon ? typeof(Texture2D).IsAssignableFrom(typeof(T)) : typeof(T) == typeof(string);
            error = accepted ? null : $"Item {(isIcon ? "icons must be a Texture2D" : "text must be text")}, not {typeof(T).Name}.";
            return accepted;
        }

        public void SetValue<T>(T value)
        {
            if (isIcon)
            {
                this.SetIcon(value as Texture2D);
            }
            else
            {
                this.SetText(value as string ?? "");
            }
        }

        public void SetFallback()
        {
            if (isIcon)
            {
                this.SetIcon(null);
            }
            else
            {
                this.SetText("");
            }
        }

        public bool TryReadValue<T>(out T value)
        {
            value = default!;
            return false;
        }

        public IDisposable? ObserveChanges(Action onChanged) => null;

        private void SetText(string text)
        {
            entry.Text = text;
            int index = list.entries.IndexOf(entry);
            if (index >= 0)
            {
                list.SetItemText(index, text);
            }
        }

        private void SetIcon(Texture2D? icon)
        {
            entry.Icon = icon;
            int index = list.entries.IndexOf(entry);
            if (index >= 0)
            {
                list.SetItemIcon(index, icon);
            }
        }
    }
}

internal sealed class ItemListItemsTarget(ItemList list, BindingDefBase definition, DataContext parentContext)
    : ListItemsTarget(list, definition, parentContext)
{
    public override string SelectionSignal => "item_selected";

    protected override int SelectedIndex => list.GetSelectedItems() is { Length: > 0 } selected ? selected[0] : -1;

    protected override void InsertItem(int index)
    {
        list.AddItem("");
        list.MoveItem(list.ItemCount - 1, index);
    }

    protected override void RemoveItem(int index) => list.RemoveItem(index);

    protected override void MoveItem(int oldIndex, int newIndex) => list.MoveItem(oldIndex, newIndex);

    protected override void SetItemText(int index, string text) => list.SetItemText(index, text);

    protected override void SetItemIcon(int index, Texture2D? icon) => list.SetItemIcon(index, icon);

    protected override void ClearItems() => list.Clear();

    protected override void Select(int index)
    {
        if (index < 0)
        {
            list.DeselectAll();
        }
        else
        {
            list.Select(index);
        }
    }
}

// OptionButton can only append and remove items, so its items are rewritten from the entries after each change.
internal sealed class OptionButtonItemsTarget(OptionButton option, BindingDefBase definition, DataContext parentContext)
    : ListItemsTarget(option, definition, parentContext)
{
    public override string SelectionSignal => "item_selected";

    protected override int SelectedIndex => option.Selected;

    protected override void InsertItem(int index) => option.AddItem("");

    protected override void RemoveItem(int index) => option.RemoveItem(option.ItemCount - 1);

    protected override void MoveItem(int oldIndex, int newIndex)
    {
    }

    protected override void SetItemText(int index, string text) => option.SetItemText(index, text);

    protected override void SetItemIcon(int index, Texture2D? icon) => option.SetItemIcon(index, icon);

    protected override void ClearItems() => option.Clear();

    protected override void Select(int index) => option.Select(index);

    protected override void StructureChanged()
    {
        object? selected = this.SelectedItem;
        for (int i = 0; i < this.Entries.Count; i++)
        {
            option.SetItemText(i, this.Entries[i].Text);
            option.SetItemIcon(i, this.Entries[i].Icon);
        }

        this.SelectItem(selected);
    }
}

// A Tree shows the items as rows under a hidden root, with their text and icon in the first column.
internal sealed class TreeItemsTarget : ListItemsTarget
{
    private readonly Tree tree;
    private readonly TreeItem root;

    public TreeItemsTarget(Tree tree, BindingDefBase definition, DataContext parentContext)
        : base(tree, definition, parentContext)
    {
        this.tree = tree;
        tree.HideRoot = true;
        this.root = tree.GetRoot() ?? tree.CreateItem();
    }

    public override string SelectionSignal => "item_selected";

    protected override int SelectedIndex => this.tree.GetSelected() is { } selected && selected.GetParent() == this.root ? selected.GetIndex() : -1;

    protected override void InsertItem(int index) => this.tree.CreateItem(this.root, index);

    protected override void RemoveItem(int index) => this.root.GetChild(index).Free();

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        TreeItem item = this.root.GetChild(oldIndex);
        TreeItem[] others = this.root.GetChildren().Where(child => child != item).ToArray();
        if (newIndex == 0)
        {
            item.MoveBefore(others[0]);
        }
        else
        {
            item.MoveAfter(others[newIndex - 1]);
        }
    }

    protected override void SetItemText(int index, string text) => this.root.GetChild(index).SetText(0, text);

    protected override void SetItemIcon(int index, Texture2D? icon) => this.root.GetChild(index).SetIcon(0, icon);

    protected override void ClearItems()
    {
        foreach (TreeItem child in this.root.GetChildren())
        {
            child.Free();
        }
    }

    protected override void Select(int index)
    {
        if (index < 0)
        {
            this.tree.DeselectAll();
        }
        else
        {
            this.root.GetChild(index).Select(0);
        }
    }
}

// The selected item of a list control, two-way: set to one of the items to select it.
internal sealed class ListSelectionTarget(ListItemsTarget items, Node node) : IBindingTarget
{
    public string Description => $"{node.GetPath()}:{BindingRootBase.SelectedKey}";

    public bool CanAccept<T>([NotNullWhen(false)] out string? error)
    {
        error = null;
        return true;
    }

    public void SetValue<T>(T value) => items.SelectItem(value);

    public void SetFallback() => items.SelectItem(null);

    public bool TryReadValue<T>(out T value)
    {
        switch (items.SelectedItem)
        {
            case T selected:
                value = selected;
                return true;
            case null when default(T) is null:
                value = default!;
                return true;
            default:
                value = default!;
                return false;
        }
    }

    public IDisposable? ObserveChanges(Action onChanged) =>
        SignalConnection.TryConnect(node, items.SelectionSignal, onChanged, out SignalConnection? connection, out _) ? connection : null;
}

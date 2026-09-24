using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Godot;
using R3;

namespace GodotHat.Binding;

// Matches items when a collection is reset: the same instance for reference types, equal values for value types.
internal sealed class ItemKeyComparer : IEqualityComparer<object>
{
    public static readonly ItemKeyComparer Instance = new();

    public new bool Equals(object? x, object? y) => x is ValueType ? x.Equals(y) : ReferenceEquals(x, y);

    public int GetHashCode(object obj) => obj is ValueType ? obj.GetHashCode() : RuntimeHelpers.GetHashCode(obj);
}

// A container whose children are instances of a template scene, one per item, with the item as their data context.
// Rows are placed after the container's own children, reused by item when the collection is reset, and pooled when
// removed. Replacing an item updates its row's context rather than instantiating a new one.
internal sealed class ContainerItemsTarget(
    BindingRootBase root,
    Node container,
    BindingDefBase definition,
    DataContext parentContext) : IItemsTarget, IDisposable
{
    private const int MaxPooled = 32;

    private readonly List<Row> rows = new();
    private readonly Stack<Row> pool = new();
    private readonly int offset = container.GetChildCount();
    private string? description;

    public string Description => this.description ??= $"{container.GetPath()}:{BindingRootBase.ItemsKey}";

    public IReadOnlyList<Node> Instances => this.rows.ConvertAll(row => row.Instance);

    public bool CanAccept<TItem>([NotNullWhen(false)] out string? error)
    {
        error = definition.Template is null ? $"'{BindingRootBase.ItemsKey}' on {container.GetClass()} needs a Template scene." : null;
        return error is null;
    }

    public void Reset<TItem>(ReadOnlySpan<TItem> items)
    {
        var available = new Dictionary<object, Queue<Row>>(ItemKeyComparer.Instance);
        foreach (Row row in this.rows)
        {
            if (row.Item.Value is { } key)
            {
                if (!available.TryGetValue(key, out Queue<Row>? queue))
                {
                    available[key] = queue = new Queue<Row>();
                }

                queue.Enqueue(row);
            }
        }

        var next = new List<Row>(items.Length);
        var kept = new HashSet<Row>();
        foreach (TItem item in items)
        {
            if (item is not null && available.TryGetValue(item, out Queue<Row>? matches) && matches.TryDequeue(out Row? reused))
            {
                kept.Add(reused);
                next.Add(reused);
            }
            else
            {
                next.Add(this.CreateRow(item));
            }
        }

        foreach (Row row in this.rows)
        {
            if (!kept.Contains(row))
            {
                this.Release(row);
            }
        }

        this.rows.Clear();
        this.rows.AddRange(next);
        for (int i = 0; i < this.rows.Count; i++)
        {
            this.Place(this.rows[i], i);
        }
    }

    public void Insert<TItem>(int index, ReadOnlySpan<TItem> items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            Row row = this.CreateRow(items[i]);
            this.rows.Insert(index + i, row);
            this.Place(row, index + i);
        }
    }

    public void Remove(int index, int count)
    {
        for (int i = 0; i < count; i++)
        {
            this.Release(this.rows[index + i]);
        }

        this.rows.RemoveRange(index, count);
    }

    public void Move(int oldIndex, int newIndex)
    {
        Row row = this.rows[oldIndex];
        this.rows.RemoveAt(oldIndex);
        this.rows.Insert(newIndex, row);
        container.MoveChild(row.Instance, this.offset + newIndex);
    }

    public void Replace<TItem>(int index, TItem item) => this.rows[index].Item.Value = item;

    public void Dispose()
    {
        foreach (Row row in this.rows)
        {
            this.Release(row);
        }

        this.rows.Clear();
        while (this.pool.TryPop(out Row? row))
        {
            row.Free();
        }
    }

    private Row CreateRow<TItem>(TItem item)
    {
        if (!this.pool.TryPop(out Row? row))
        {
            var values = new ReactiveProperty<object?>(null, ReferenceEqualityComparer.Instance);
            row = new Row(definition.Template!.Instantiate(), values, DataContext.Create<TItem>(values, parentContext));
        }

        row.Item.Value = item;
        root.RegisterItemContext(row.Instance, row.Context);
        return row;
    }

    // Adding the instance binds it, with its registered item context, through the container's child-entered hook.
    private void Place(Row row, int index)
    {
        if (row.Instance.GetParent() != container)
        {
            container.AddChild(row.Instance);
        }

        container.MoveChild(row.Instance, this.offset + index);
    }

    private void Release(Row row)
    {
        if (GodotObject.IsInstanceValid(container) && GodotObject.IsInstanceValid(row.Instance) && row.Instance.GetParent() == container)
        {
            container.RemoveChild(row.Instance);
        }

        root.UnregisterItemContext(row.Instance);
        if (this.pool.Count < MaxPooled && GodotObject.IsInstanceValid(row.Instance))
        {
            row.Item.Value = null;
            this.pool.Push(row);
        }
        else
        {
            row.Free();
        }
    }

    private sealed class Row(Node instance, ReactiveProperty<object?> item, DataContext context)
    {
        public Node Instance { get; } = instance;
        public ReactiveProperty<object?> Item { get; } = item;
        public DataContext Context { get; } = context;

        public void Free()
        {
            this.Item.Dispose();
            if (GodotObject.IsInstanceValid(this.Instance))
            {
                this.Instance.QueueFree();
            }
        }
    }
}

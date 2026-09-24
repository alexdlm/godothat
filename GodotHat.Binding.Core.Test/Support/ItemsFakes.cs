using System.Diagnostics.CodeAnalysis;
using ObservableCollections;
using R3;

namespace GodotHat.Binding.Core.Test.Support;

public sealed class InventoryViewModel
{
    public ObservableList<string> Items { get; } = new();
    public ObservableHashSet<int> Tags { get; } = new();
    public ObservableDictionary<string, int> Stock { get; } = new();
    public ObservableQueue<int> Queue { get; } = new();
    public ObservableStack<int> Stack { get; } = new();
    public ObservableFixedSizeRingBuffer<int> Recent { get; } = new(3);
    public ISynchronizedView<string, string> Shouted { get; }
    public ReactiveProperty<InventoryViewModel?> Other { get; } = new();

    public InventoryViewModel()
    {
        this.Shouted = this.Items.CreateView(item => item.ToUpperInvariant());
    }

    public static readonly ViewModelAccessor Accessor = new(
        typeof(InventoryViewModel),
        [
            new CollectionItemsMember<InventoryViewModel, string>("Items", static vm => vm.Items),
            new CollectionItemsMember<InventoryViewModel, int>("Tags", static vm => vm.Tags),
            new CollectionItemsMember<InventoryViewModel, KeyValuePair<string, int>>("Stock", static vm => vm.Stock),
            new CollectionItemsMember<InventoryViewModel, int>("Queue", static vm => vm.Queue),
            new CollectionItemsMember<InventoryViewModel, int>("Stack", static vm => vm.Stack),
            new CollectionItemsMember<InventoryViewModel, int>("Recent", static vm => vm.Recent),
            new ViewItemsMember<InventoryViewModel, string, string>("Shouted", static vm => vm.Shouted),
            new PropertyMember<InventoryViewModel, InventoryViewModel?>("Other", MemberKind.ReactiveProperty, static vm => vm.Other),
        ]);
}

// Keeps a list in step with the operations it receives, and logs them.
public sealed class FakeItemsTarget : IItemsTarget
{
    public string Description => "container";
    public List<object?> Items { get; } = [];
    public List<string> Log { get; } = [];
    public int ThreadId { get; private set; }

    public bool CanAccept<TItem>([NotNullWhen(false)] out string? error)
    {
        error = null;
        return true;
    }

    public void Reset<TItem>(ReadOnlySpan<TItem> items)
    {
        this.Record($"reset {items.Length}");
        this.Items.Clear();
        foreach (TItem item in items)
        {
            this.Items.Add(item);
        }
    }

    public void Insert<TItem>(int index, ReadOnlySpan<TItem> items)
    {
        this.Record($"insert {index} {items.Length}");
        this.Items.InsertRange(index, items.ToArray().Cast<object?>());
    }

    public void Remove(int index, int count)
    {
        this.Record($"remove {index} {count}");
        this.Items.RemoveRange(index, count);
    }

    public void Move(int oldIndex, int newIndex)
    {
        this.Record($"move {oldIndex} {newIndex}");
        object? item = this.Items[oldIndex];
        this.Items.RemoveAt(oldIndex);
        this.Items.Insert(newIndex, item);
    }

    public void Replace<TItem>(int index, TItem item)
    {
        this.Record($"replace {index}");
        this.Items[index] = item;
    }

    private void Record(string operation)
    {
        this.Log.Add(operation);
        this.ThreadId = Environment.CurrentManagedThreadId;
    }
}

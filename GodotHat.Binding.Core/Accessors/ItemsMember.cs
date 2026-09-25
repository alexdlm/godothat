using System.Runtime.InteropServices;
using ObservableCollections;
using R3;

namespace GodotHat.Binding;

/// <summary>
/// Receives a collection's contents and changes as ordered operations. Called on the thread that changed the
/// collection, while it is locked.
/// </summary>
public interface IItemsObserver<TItem>
{
    /// <summary>The collection now contains <paramref name="items"/>, eg when first observed, cleared or sorted.</summary>
    void OnReset(ReadOnlySpan<TItem> items);

    /// <summary><paramref name="items"/> were inserted at <paramref name="index"/>.</summary>
    void OnInsert(int index, ReadOnlySpan<TItem> items);

    /// <summary><paramref name="count"/> items were removed from <paramref name="index"/>.</summary>
    void OnRemove(int index, int count);

    /// <summary>The item at <paramref name="oldIndex"/> was removed and inserted at <paramref name="newIndex"/>.</summary>
    void OnMove(int oldIndex, int newIndex);

    /// <summary>The item at <paramref name="index"/> was replaced.</summary>
    void OnReplace(int index, TItem item);
}

/// <summary>A collection member of a view model, whose items are <typeparamref name="TItem"/>.</summary>
public abstract class ItemsMember<TItem> : MemberAccessor
{
    /// <summary>Creates the member.</summary>
    protected ItemsMember(string name) : base(name, MemberKind.Items, typeof(TItem))
    {
    }

    /// <summary>
    /// Observes the collection on <paramref name="owner"/>: reports its current items with
    /// <see cref="IItemsObserver{TItem}.OnReset"/>, then each change, until disposed.
    /// </summary>
    public abstract IDisposable Subscribe(object owner, IItemsObserver<TItem> observer);

    /// <summary>The number of items in the collection on <paramref name="owner"/>, now and after each change.</summary>
    public abstract Observable<int> ObserveCount(object owner);

    /// <inheritdoc />
    public override TResult Accept<TResult>(IMemberVisitor<TResult> visitor) => visitor.VisitItems(this);
}

/// <summary>An ObservableCollections collection, such as an <c>ObservableList&lt;T&gt;</c>.</summary>
public sealed class CollectionItemsMember<TOwner, T>(string name, Func<TOwner, IObservableCollection<T>> get)
    : ItemsMember<T>(name)
    where TOwner : class
{
    /// <inheritdoc />
    public override IDisposable Subscribe(object owner, IItemsObserver<T> observer) =>
        new CollectionSubscription<T>(get((TOwner)owner), observer);

    /// <inheritdoc />
    public override Observable<int> ObserveCount(object owner) => new CollectionCount<T>(get((TOwner)owner));
}

/// <summary>
/// An ObservableCollections view, typically filtered or transformed with <c>CreateView</c>; items are the views.
/// </summary>
public sealed class ViewItemsMember<TOwner, T, TView>(string name, Func<TOwner, ISynchronizedView<T, TView>> get)
    : ItemsMember<TView>(name)
    where TOwner : class
{
    /// <inheritdoc />
    public override IDisposable Subscribe(object owner, IItemsObserver<TView> observer) =>
        new ViewSubscription<T, TView>(get((TOwner)owner), observer);

    /// <inheritdoc />
    public override Observable<int> ObserveCount(object owner) => new ViewCount<T, TView>(get((TOwner)owner));
}

// Translates a collection's change events into ordered operations, keeping a mirror of its items to place changes
// from unordered collections (sets and dictionaries report no indices) and to resynchronise after sorting.
internal sealed class CollectionSubscription<T> : IDisposable
{
    private readonly IObservableCollection<T> collection;
    private readonly IItemsObserver<T> observer;
    private readonly List<T> mirror;
    private readonly bool reversedRanges;

    public CollectionSubscription(IObservableCollection<T> collection, IItemsObserver<T> observer)
    {
        this.collection = collection;
        this.observer = observer;

        // A stack enumerates from the top, but reports pushed ranges in push order
        this.reversedRanges = collection is ObservableStack<T>;
        lock (collection.SyncRoot)
        {
            collection.CollectionChanged += this.OnChanged;
            this.mirror = new List<T>(collection);
            observer.OnReset(CollectionsMarshal.AsSpan(this.mirror));
        }
    }

    public void Dispose()
    {
        lock (this.collection.SyncRoot)
        {
            this.collection.CollectionChanged -= this.OnChanged;
        }
    }

    private void OnChanged(in NotifyCollectionChangedEventArgs<T> e)
    {
        switch (e.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                if (e.IsSingleItem)
                {
                    T item = e.NewItem;
                    this.Insert(e.NewStartingIndex, new ReadOnlySpan<T>(ref item));
                }
                else
                {
                    this.Insert(e.NewStartingIndex, e.NewItems);
                }

                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                if (e.IsSingleItem)
                {
                    T item = e.OldItem;
                    this.Remove(e.OldStartingIndex, new ReadOnlySpan<T>(ref item));
                }
                else
                {
                    this.Remove(e.OldStartingIndex, e.OldItems);
                }

                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                int index = e.NewStartingIndex >= 0 ? e.NewStartingIndex : this.mirror.IndexOf(e.OldItem);
                if (index < 0 || index >= this.mirror.Count)
                {
                    this.Resynchronise();
                    break;
                }

                this.mirror[index] = e.NewItem;
                this.observer.OnReplace(index, e.NewItem);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                T moved = this.mirror[e.OldStartingIndex];
                this.mirror.RemoveAt(e.OldStartingIndex);
                this.mirror.Insert(e.NewStartingIndex, moved);
                this.observer.OnMove(e.OldStartingIndex, e.NewStartingIndex);
                break;
            default:
                this.Resynchronise();
                break;
        }
    }

    private void Insert(int index, ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
        {
            return;
        }

        if (index < 0)
        {
            index = this.mirror.Count;
        }

        if (this.reversedRanges && items.Length > 1)
        {
            T[] reversed = items.ToArray();
            Array.Reverse(reversed);
            items = reversed;
        }

        this.mirror.InsertRange(index, items);
        this.observer.OnInsert(index, items);
    }

    private void Remove(int index, ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
        {
            return;
        }

        if (index >= 0)
        {
            this.mirror.RemoveRange(index, items.Length);
            this.observer.OnRemove(index, items.Length);
            return;
        }

        foreach (T item in items)
        {
            int found = this.mirror.IndexOf(item);
            if (found >= 0)
            {
                this.mirror.RemoveAt(found);
                this.observer.OnRemove(found, 1);
            }
        }
    }

    private void Resynchronise()
    {
        this.mirror.Clear();
        this.mirror.AddRange(this.collection);
        this.observer.OnReset(CollectionsMarshal.AsSpan(this.mirror));
    }
}

// A view's visible items as a reset on each change. View events report indices in the unfiltered collection and
// omit rejected items, so the visible list is re-read rather than patched; targets reuse existing items when reset.
internal sealed class ViewSubscription<T, TView> : IDisposable
{
    private readonly ISynchronizedView<T, TView> view;
    private readonly IItemsObserver<TView> observer;
    private readonly List<TView> snapshot = new();

    public ViewSubscription(ISynchronizedView<T, TView> view, IItemsObserver<TView> observer)
    {
        this.view = view;
        this.observer = observer;
        lock (view.SyncRoot)
        {
            view.ViewChanged += this.OnChanged;
            this.Resynchronise();
        }
    }

    public void Dispose()
    {
        lock (this.view.SyncRoot)
        {
            this.view.ViewChanged -= this.OnChanged;
        }
    }

    private void OnChanged(in SynchronizedViewChangedEventArgs<T, TView> e) => this.Resynchronise();

    private void Resynchronise()
    {
        this.snapshot.Clear();
        this.snapshot.AddRange(this.view);
        this.observer.OnReset(CollectionsMarshal.AsSpan(this.snapshot));
    }
}

internal sealed class CollectionCount<T>(IObservableCollection<T> collection) : Observable<int>
{
    protected override IDisposable SubscribeCore(Observer<int> observer)
    {
        NotifyCollectionChangedEventHandler<T> handler = (in NotifyCollectionChangedEventArgs<T> _) => observer.OnNext(collection.Count);
        lock (collection.SyncRoot)
        {
            collection.CollectionChanged += handler;
            observer.OnNext(collection.Count);
        }

        return Disposable.Create(() =>
        {
            lock (collection.SyncRoot)
            {
                collection.CollectionChanged -= handler;
            }
        });
    }
}

internal sealed class ViewCount<T, TView>(ISynchronizedView<T, TView> view) : Observable<int>
{
    protected override IDisposable SubscribeCore(Observer<int> observer)
    {
        NotifyViewChangedEventHandler<T, TView> handler = (in SynchronizedViewChangedEventArgs<T, TView> _) => observer.OnNext(view.Count);
        lock (view.SyncRoot)
        {
            view.ViewChanged += handler;
            observer.OnNext(view.Count);
        }

        return Disposable.Create(() =>
        {
            lock (view.SyncRoot)
            {
                view.ViewChanged -= handler;
            }
        });
    }
}

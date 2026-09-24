using System.Buffers;
using R3;

namespace GodotHat.Binding;

public sealed partial class BindingEngine
{
    // Follows the collection at the end of the binding's path and replays its changes on the target, on the main
    // thread and in order. Changes made on the main thread apply immediately when nothing is queued; others are copied
    // (the collection's spans are only valid during its event) and applied when the dispatcher runs.
    private sealed class ItemsBinding<TItem>(
        BindingEngine engine,
        BindingSpec spec,
        IItemsTarget target,
        ItemsMember<TItem> member) : IItemsObserver<TItem>, IDispatchWork, IDisposable
    {
        private readonly object gate = new();
        private readonly Queue<Operation> queue = new();
        private IDisposable? owners;
        private SerialDisposableCore collection;
        private bool posted;
        private bool disposed;

        private enum OperationKind
        {
            Reset,
            Insert,
            Remove,
            Move,
            Replace,
        }

        public void Start(Observable<object?> ownerSource) =>
            this.owners = ownerSource.Subscribe(this, static (owner, binding) => binding.OnOwner(owner));

        public void Dispose()
        {
            lock (this.gate)
            {
                this.disposed = true;
                while (this.queue.TryDequeue(out Operation operation))
                {
                    operation.Return();
                }
            }

            this.owners?.Dispose();
            this.collection.Dispose();
        }

        public void OnReset(ReadOnlySpan<TItem> items) => this.Deliver(OperationKind.Reset, 0, 0, items, default!);

        public void OnInsert(int index, ReadOnlySpan<TItem> items) => this.Deliver(OperationKind.Insert, index, 0, items, default!);

        public void OnRemove(int index, int count) => this.Deliver(OperationKind.Remove, index, count, default, default!);

        public void OnMove(int oldIndex, int newIndex) => this.Deliver(OperationKind.Move, oldIndex, newIndex, default, default!);

        public void OnReplace(int index, TItem item) => this.Deliver(OperationKind.Replace, index, 0, default, item);

        public void Run()
        {
            while (true)
            {
                Operation operation;
                lock (this.gate)
                {
                    if (this.disposed || !this.queue.TryDequeue(out operation))
                    {
                        this.posted = false;
                        return;
                    }
                }

                try
                {
                    this.Apply(operation.Kind, operation.Index, operation.Other, operation.Span, operation.Item);
                }
                finally
                {
                    operation.Return();
                }
            }
        }

        private void OnOwner(object? owner)
        {
            this.collection.Disposable = null;
            if (owner is null)
            {
                this.OnReset(ReadOnlySpan<TItem>.Empty);
                return;
            }

            try
            {
                this.collection.Disposable = member.Subscribe(owner, this);
            }
            catch (Exception e)
            {
                engine.Report(target.Description, spec, "Failed to observe the collection.", e);
                this.OnReset(ReadOnlySpan<TItem>.Empty);
            }
        }

        private void Deliver(OperationKind kind, int index, int other, ReadOnlySpan<TItem> items, TItem item)
        {
            bool post;
            lock (this.gate)
            {
                if (this.disposed)
                {
                    return;
                }

                if (!this.posted && engine.Dispatcher.IsMainThread)
                {
                    post = false;
                }
                else
                {
                    this.queue.Enqueue(Operation.Copy(kind, index, other, items, item));
                    post = !this.posted;
                    this.posted = true;
                    if (post)
                    {
                        engine.Dispatcher.Post(this);
                    }

                    return;
                }
            }

            this.Apply(kind, index, other, items, item);
        }

        private void Apply(OperationKind kind, int index, int other, ReadOnlySpan<TItem> items, TItem item)
        {
            try
            {
                switch (kind)
                {
                    case OperationKind.Reset:
                        target.Reset(items);
                        break;
                    case OperationKind.Insert:
                        target.Insert(index, items);
                        break;
                    case OperationKind.Remove:
                        target.Remove(index, other);
                        break;
                    case OperationKind.Move:
                        target.Move(index, other);
                        break;
                    case OperationKind.Replace:
                        target.Replace(index, item);
                        break;
                }
            }
            catch (Exception e)
            {
                engine.Report(target.Description, spec, $"Failed to apply a collection {kind}.", e);
            }
        }

        private readonly struct Operation(OperationKind kind, int index, int other, TItem[]? items, int count, TItem item)
        {
            public OperationKind Kind { get; } = kind;
            public int Index { get; } = index;
            public int Other { get; } = other;
            public TItem Item { get; } = item;

            public ReadOnlySpan<TItem> Span => items is null ? default : items.AsSpan(0, count);

            public static Operation Copy(OperationKind kind, int index, int other, ReadOnlySpan<TItem> items, TItem item)
            {
                TItem[]? copy = null;
                if (!items.IsEmpty)
                {
                    copy = ArrayPool<TItem>.Shared.Rent(items.Length);
                    items.CopyTo(copy);
                }

                return new Operation(kind, index, other, copy, items.Length, item);
            }

            public void Return()
            {
                if (items is not null)
                {
                    ArrayPool<TItem>.Shared.Return(items, clearArray: true);
                }
            }
        }
    }
}

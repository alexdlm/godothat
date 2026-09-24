using Godot;
using R3;

namespace GodotHat.Binding;

// Releases a data context whose value is a node when that node leaves the tree, so bindings fall back rather than
// keep reading a node that may be freed without disposing its reactive members.
internal static class NodeContextGuard
{
    public static DataContext Guard(DataContext context, CompositeDisposable subscriptions)
    {
        if (!context.Type.IsAssignableFrom(typeof(Node)) && !typeof(Node).IsAssignableFrom(context.Type))
        {
            return context;
        }

        var guarded = new ReactiveProperty<object?>(null, ReferenceEqualityComparer.Instance).AddTo(subscriptions);
        var watcher = new Watcher(guarded).AddTo(subscriptions);
        context.Values.Subscribe(watcher, static (value, w) => w.Set(value)).AddTo(subscriptions);
        return new DataContext(context.Type, guarded, context.Parent);
    }

    private sealed class Watcher(ReactiveProperty<object?> guarded) : IDisposable
    {
        private Node? watched;

        public void Set(object? value)
        {
            this.Unwatch();
            guarded.Value = value;
            if (value is Node node && GodotObject.IsInstanceValid(node))
            {
                this.watched = node;
                node.TreeExiting += this.OnTreeExiting;
            }
        }

        public void Dispose() => this.Unwatch();

        private void OnTreeExiting()
        {
            Node? node = this.watched;
            this.Unwatch();
            if (ReferenceEquals(guarded.Value, node))
            {
                guarded.Value = null;
            }
        }

        private void Unwatch()
        {
            if (this.watched is { } node && GodotObject.IsInstanceValid(node))
            {
                node.TreeExiting -= this.OnTreeExiting;
            }

            this.watched = null;
        }
    }
}

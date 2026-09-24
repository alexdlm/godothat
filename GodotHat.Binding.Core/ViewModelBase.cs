using R3;

namespace GodotHat.Binding;

/// <summary>
/// Optional base class for plain view models, providing a <see cref="Bag"/> for subscriptions and reactive members
/// that are disposed with the view model. Not for Godot nodes, which are already <see cref="IDisposable"/>.
/// </summary>
/// <example>
/// <code>
/// HasOrders = orders.ObserveCountChanged(notifyCurrentCount: true)
///     .Select(c => c > 0).ToReadOnlyReactiveProperty().AddTo(Bag);
/// </code>
/// </example>
public abstract class ViewModelBase : IDisposable
{
    /// <summary>Disposables owned by this view model, disposed with it. Safe to add to from any thread.</summary>
    protected CompositeDisposable Bag { get; } = new();

    /// <summary>Whether <see cref="Dispose()"/> has been called.</summary>
    public bool IsDisposed => this.Bag.IsDisposed;

    /// <summary>Disposes everything in <see cref="Bag"/>.</summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Override to release other resources; call the base implementation to dispose <see cref="Bag"/>.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.Bag.Dispose();
        }
    }
}

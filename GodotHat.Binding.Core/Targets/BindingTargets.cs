using System.Diagnostics.CodeAnalysis;

namespace GodotHat.Binding;

/// <summary>A target property, as seen by the binding engine. Implemented by the Godot adapter, and by fakes in tests.</summary>
public interface IBindingTarget
{
    /// <summary>Describes the target for errors, eg a node path and property.</summary>
    string Description { get; }

    /// <summary>
    /// Whether values of <typeparamref name="T"/> can be applied, checked once when binding so updates can't fail on
    /// type.
    /// </summary>
    bool CanAccept<T>([NotNullWhen(false)] out string? error);

    /// <summary>Applies a value. Called on the main thread.</summary>
    void SetValue<T>(T value);

    /// <summary>Applies the fallback, because the binding's path can't be resolved. Called on the main thread.</summary>
    void SetFallback();

    /// <summary>Reads the target's current value, for two-way bindings.</summary>
    bool TryReadValue<T>(out T value);

    /// <summary>
    /// Calls <paramref name="onChanged"/> whenever the target changes other than through <see cref="SetValue{T}"/>,
    /// for two-way bindings; null if the target has no change notification.
    /// </summary>
    IDisposable? ObserveChanges(Action onChanged);
}

/// <summary>A target signal that executes a command, as seen by the binding engine.</summary>
public interface ICommandTarget
{
    /// <summary>Describes the target for errors, eg a node path and signal.</summary>
    string Description { get; }

    /// <summary>Calls <paramref name="onInvoked"/> each time the signal is emitted.</summary>
    IDisposable ObserveInvoked(Action onInvoked);

    /// <summary>Reflects the command's availability, eg by disabling a button. Called on the main thread.</summary>
    void SetAvailable(bool available);
}

/// <summary>A target method called for each value of a view model observable, as seen by the binding engine.</summary>
public interface IEventTarget
{
    /// <summary>Describes the target for errors, eg a node path and method.</summary>
    string Description { get; }

    /// <summary>Whether payloads of <typeparamref name="T"/> can be passed, checked once when binding.</summary>
    bool CanAccept<T>([NotNullWhen(false)] out string? error);

    /// <summary>
    /// Calls the target with <paramref name="payload"/>, or with no arguments when <typeparamref name="T"/> is
    /// <see cref="R3.Unit"/>. Called on the main thread.
    /// </summary>
    void Invoke<T>(T payload);
}

/// <summary>
/// A container whose children follow a collection, as seen by the binding engine. Operations arrive on the main thread
/// in order, and use the collection's indices.
/// </summary>
public interface IItemsTarget
{
    /// <summary>Describes the target for errors, eg a node path.</summary>
    string Description { get; }

    /// <summary>Whether items of <typeparamref name="TItem"/> can be shown, checked once when binding.</summary>
    bool CanAccept<TItem>([NotNullWhen(false)] out string? error);

    /// <summary>Shows exactly <paramref name="items"/>, reusing what's already shown for items that remain.</summary>
    void Reset<TItem>(ReadOnlySpan<TItem> items);

    /// <summary>Inserts <paramref name="items"/> at <paramref name="index"/>.</summary>
    void Insert<TItem>(int index, ReadOnlySpan<TItem> items);

    /// <summary>Removes <paramref name="count"/> items from <paramref name="index"/>.</summary>
    void Remove(int index, int count);

    /// <summary>Removes the item at <paramref name="oldIndex"/> and inserts it at <paramref name="newIndex"/>.</summary>
    void Move(int oldIndex, int newIndex);

    /// <summary>Replaces the item at <paramref name="index"/>.</summary>
    void Replace<TItem>(int index, TItem item);
}

using R3;

namespace GodotHat.Binding;

/// <summary>
/// The source that bindings in a subtree resolve their paths against: an observable view model (or item) of a
/// declared type, plus the context above it.
/// </summary>
public sealed class DataContext
{
    /// <summary>Creates a context whose values are declared as <paramref name="type"/>.</summary>
    /// <param name="type">
    /// The declared type of the values, whose accessor resolves binding paths. Values may be subclasses of it.
    /// </param>
    /// <param name="values">
    /// The current context value, emitted on subscribe and whenever it changes; null when there is none.
    /// </param>
    /// <param name="parent">The context above this one, for <see cref="BindingSourceKind.ParentContext"/>.</param>
    public DataContext(Type type, Observable<object?> values, DataContext? parent = null)
        : this(type, values, parent, SelfMember<object>.Instance)
    {
    }

    internal DataContext(Type type, Observable<object?> values, DataContext? parent, ValueMember self)
    {
        this.Type = type;
        this.Values = values;
        this.Parent = parent;
        this.Self = self;
    }

    /// <summary>The declared type of the context's values.</summary>
    public Type Type { get; }

    /// <summary>The current context value, emitted on subscribe and whenever it changes.</summary>
    public Observable<object?> Values { get; }

    /// <summary>The context above this one.</summary>
    public DataContext? Parent { get; }

    // Reads the context value itself, with its static type where known, for bindings with an empty path.
    internal ValueMember Self { get; }

    /// <summary>A context with a fixed value, declared as the value's runtime type.</summary>
    public static DataContext Constant(object value, DataContext? parent = null) =>
        new(value.GetType(), Observable.Return<object?>(value), parent);

    /// <summary>
    /// A context whose values are declared as <typeparamref name="T"/>, so bindings with an empty path bind the value
    /// itself with that type; eg an item template's context, which changes when its item is replaced.
    /// </summary>
    public static DataContext Create<T>(Observable<object?> values, DataContext? parent = null) =>
        new(typeof(T), values, parent, SelfMember<T>.Instance);

    /// <summary>A context for a single item of type <typeparamref name="T"/>, eg in an item template.</summary>
    public static DataContext ForItem<T>(T item, DataContext? parent = null) =>
        new(typeof(T), Observable.Return<object?>(item), parent, SelfMember<T>.Instance);

    /// <inheritdoc />
    public override string ToString() => $"DataContext<{this.Type.Name}>";
}

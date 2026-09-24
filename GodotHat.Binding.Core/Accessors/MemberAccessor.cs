using R3;

namespace GodotHat.Binding;

/// <summary>How a view model member is exposed, which decides the bindings it supports.</summary>
public enum MemberKind
{
    /// <summary>A writable reactive property; supports one-way and two-way bindings.</summary>
    ReactiveProperty = 0,

    /// <summary>A read-only reactive property; supports one-way bindings.</summary>
    ReadOnlyProperty = 1,

    /// <summary>An observable with no current value; supports event bindings, or one-way bindings from its first value.</summary>
    Observable = 2,

    /// <summary>A plain get-only property, read once.</summary>
    Constant = 3,

    /// <summary>A command; supports command bindings, with availability mapped to the target.</summary>
    Command = 4,

    /// <summary>An observable collection; supports item bindings.</summary>
    Items = 5,
}

/// <summary>
/// Receives a member's concrete type parameters, so bindings can be built with typed, unboxed observables. See
/// <see cref="MemberAccessor.Accept{TResult}"/>.
/// </summary>
public interface IMemberVisitor<out TResult>
{
    /// <summary>Visits a member whose values are observed as <typeparamref name="T"/>.</summary>
    TResult VisitValue<T>(ValueMember<T> member);

    /// <summary>Visits a command taking <typeparamref name="TParam"/>.</summary>
    TResult VisitCommand<TParam>(CommandMember<TParam> member);

    /// <summary>Visits a collection of <typeparamref name="TItem"/>.</summary>
    TResult VisitItems<TItem>(ItemsMember<TItem> member);
}

/// <summary>A bindable member of a view model, as described by its generated accessor.</summary>
public abstract class MemberAccessor
{
    private protected MemberAccessor(string name, MemberKind kind, Type valueType)
    {
        this.Name = name;
        this.Kind = kind;
        this.ValueType = valueType;
    }

    /// <summary>The member's name, as used in binding paths.</summary>
    public string Name { get; }

    /// <summary>How the member is exposed.</summary>
    public MemberKind Kind { get; }

    /// <summary>The value type for value members, parameter type for commands, or item type for collections.</summary>
    public Type ValueType { get; }

    /// <summary>Passes this member's concrete type parameters to <paramref name="visitor"/>.</summary>
    public abstract TResult Accept<TResult>(IMemberVisitor<TResult> visitor);

    /// <inheritdoc />
    public override string ToString() => $"{this.Name} ({this.Kind} {this.ValueType.Name})";
}

/// <summary>A member with observable values, whatever their type.</summary>
public abstract class ValueMember : MemberAccessor
{
    private protected ValueMember(string name, MemberKind kind, Type valueType) : base(name, kind, valueType)
    {
    }

    /// <summary>Whether values can be written back, for two-way bindings.</summary>
    public virtual bool CanWrite => false;

    // Observes this member of each owner in turn, as the owners of the next path segment. Only valid for members whose
    // values are reference types, which BindingPath checks.
    internal abstract Observable<object?> SwitchOwners(Observable<object?> owners);
}

/// <summary>A member with observable values of type <typeparamref name="T"/>.</summary>
public abstract class ValueMember<T> : ValueMember
{
    /// <summary>Creates the member.</summary>
    protected ValueMember(string name, MemberKind kind) : base(name, kind, typeof(T))
    {
    }

    /// <summary>
    /// Observes the member on <paramref name="owner"/>. Reactive properties and constants emit their current value on
    /// subscribe.
    /// </summary>
    public abstract Observable<T> Observe(object owner);

    /// <summary>Writes a value back to the member on <paramref name="owner"/>.</summary>
    public virtual void Write(object owner, T value) =>
        throw new NotSupportedException($"{this.Name} is not writable.");

    /// <inheritdoc />
    public override TResult Accept<TResult>(IMemberVisitor<TResult> visitor) => visitor.VisitValue(this);

    internal override Observable<object?> SwitchOwners(Observable<object?> owners) =>
        new MemberSwitch<T>(owners, this, null).Select(static v => v.HasValue ? (object?)v.Value : null);
}

/// <summary>
/// A member observed through a delegate, typically a reactive property or observable. Generated accessors create
/// these; they can also be written by hand, eg in tests.
/// </summary>
/// <example>
/// <code>
/// new PropertyMember&lt;CounterViewModel, int&gt;("Count", MemberKind.ReactiveProperty,
///     static vm => vm.Count, static (vm, value) => vm.Count.Value = value)
/// </code>
/// </example>
public sealed class PropertyMember<TOwner, T> : ValueMember<T>
    where TOwner : class
{
    private readonly Func<TOwner, Observable<T>> observe;
    private readonly Action<TOwner, T>? write;

    /// <summary>Creates the member; pass <paramref name="write"/> to make it writable.</summary>
    public PropertyMember(
        string name,
        MemberKind kind,
        Func<TOwner, Observable<T>> observe,
        Action<TOwner, T>? write = null) : base(name, kind)
    {
        this.observe = observe;
        this.write = write;
    }

    /// <inheritdoc />
    public override bool CanWrite => this.write is not null;

    /// <inheritdoc />
    public override Observable<T> Observe(object owner) => this.observe((TOwner)owner);

    /// <inheritdoc />
    public override void Write(object owner, T value)
    {
        if (this.write is null)
        {
            base.Write(owner, value);
            return;
        }

        this.write((TOwner)owner, value);
    }
}

/// <summary>A plain get-only property, read once per owner.</summary>
public sealed class ConstantMember<TOwner, T> : ValueMember<T>
    where TOwner : class
{
    private readonly Func<TOwner, T> get;

    /// <summary>Creates the member.</summary>
    public ConstantMember(string name, Func<TOwner, T> get) : base(name, MemberKind.Constant)
    {
        this.get = get;
    }

    /// <inheritdoc />
    public override Observable<T> Observe(object owner) => Observable.Return(this.get((TOwner)owner));
}

// A collection's item count, so collections can be bound to properties, eg with the `any` converter.
internal sealed class ItemsCountMember<TItem>(ItemsMember<TItem> items) : ValueMember<int>(items.Name, MemberKind.ReadOnlyProperty)
{
    public override Observable<int> Observe(object owner) => items.ObserveCount(owner);
}

// The data context itself, for bindings with an empty path.
internal sealed class SelfMember<T> : ValueMember<T>
{
    public static readonly SelfMember<T> Instance = new();

    private SelfMember() : base("", MemberKind.Constant)
    {
    }

    public override Observable<T> Observe(object owner) => Observable.Return((T)owner);
}

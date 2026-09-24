namespace GodotHat.Binding;

/// <summary>
/// A value flowing through a binding, or the absence of one when the binding's path can't be resolved (a null or
/// disposed member along the path), in which case targets show their fallback.
/// </summary>
public readonly struct BindingValue<T> : IEquatable<BindingValue<T>>
{
    /// <summary>Creates a present value.</summary>
    public BindingValue(T value)
    {
        this.Value = value;
        this.HasValue = true;
    }

    /// <summary>No value; the path could not be resolved.</summary>
    public static BindingValue<T> Missing => default;

    /// <summary>Whether <see cref="Value"/> is present.</summary>
    public bool HasValue { get; }

    /// <summary>The value, or <c>default</c> when <see cref="HasValue"/> is false.</summary>
    public T Value { get; }

    /// <inheritdoc />
    public bool Equals(BindingValue<T> other) =>
        this.HasValue == other.HasValue && (!this.HasValue || EqualityComparer<T>.Default.Equals(this.Value, other.Value));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BindingValue<T> other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => this.HasValue ? EqualityComparer<T>.Default.GetHashCode(this.Value!) : 0;

    /// <inheritdoc />
    public override string ToString() => this.HasValue ? $"{this.Value}" : "<missing>";

    /// <summary>Equality of presence and value.</summary>
    public static bool operator ==(BindingValue<T> left, BindingValue<T> right) => left.Equals(right);

    /// <summary>Inequality of presence or value.</summary>
    public static bool operator !=(BindingValue<T> left, BindingValue<T> right) => !left.Equals(right);
}

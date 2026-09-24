namespace GodotHat.Binding;

/// <summary>
/// Names and values of an enum in declaration order, generated for enums used by view models so enum conversions
/// need no reflection.
/// </summary>
public abstract class EnumInfo
{
    private protected EnumInfo(Type enumType, IReadOnlyList<string> names)
    {
        this.EnumType = enumType;
        this.Names = names;
    }

    /// <summary>The enum type.</summary>
    public Type EnumType { get; }

    /// <summary>Member names in declaration order.</summary>
    public IReadOnlyList<string> Names { get; }
}

/// <summary>
/// Typed access to an <see cref="EnumInfo{T}"/> from code where <typeparamref name="T"/> isn't known to be an enum.
/// </summary>
public interface IEnumInfo<T>
{
    /// <summary>The name of <paramref name="value"/>, or null if it isn't a declared member (eg combined flags).</summary>
    string? GetName(T value);

    /// <summary>The declaration index of <paramref name="value"/>, or -1.</summary>
    int IndexOf(T value);

    /// <summary>Finds the value named <paramref name="name"/>, case-sensitively.</summary>
    bool TryParse(string? name, out T value);
}

/// <summary>Names and values of <typeparamref name="T"/> in declaration order.</summary>
public sealed class EnumInfo<T> : EnumInfo, IEnumInfo<T>
    where T : struct, Enum
{
    private readonly T[] values;
    private readonly string[] names;

    /// <summary>Creates the table; <paramref name="values"/> and <paramref name="names"/> must correspond.</summary>
    public EnumInfo(T[] values, string[] names) : base(typeof(T), names)
    {
        if (values.Length != names.Length)
        {
            throw new ArgumentException("Values and names must have the same length.", nameof(names));
        }

        this.values = values;
        this.names = names;
    }

    /// <summary>Values in declaration order.</summary>
    public IReadOnlyList<T> Values => this.values;

    /// <summary>The name of <paramref name="value"/>, or null if it isn't a declared member (eg combined flags).</summary>
    public string? GetName(T value)
    {
        int index = this.IndexOf(value);
        return index >= 0 ? this.names[index] : null;
    }

    /// <summary>The declaration index of <paramref name="value"/>, or -1.</summary>
    public int IndexOf(T value)
    {
        for (int i = 0; i < this.values.Length; i++)
        {
            if (EqualityComparer<T>.Default.Equals(this.values[i], value))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Finds the value named <paramref name="name"/>, case-sensitively.</summary>
    public bool TryParse(string? name, out T value)
    {
        for (int i = 0; i < this.names.Length; i++)
        {
            if (string.Equals(this.names[i], name, StringComparison.Ordinal))
            {
                value = this.values[i];
                return true;
            }
        }

        value = default;
        return false;
    }
}

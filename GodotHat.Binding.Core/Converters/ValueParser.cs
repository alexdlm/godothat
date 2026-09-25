using System.Globalization;
using System.Runtime.CompilerServices;

namespace GodotHat.Binding;

// Parses text to a value of a type only known at runtime, without boxing: the typeof checks fold away per T when
// jitted. Used for converter arguments and constant command parameters (invariant culture), and for converting text
// typed by the user back to the source (current culture).
internal static class ValueParser
{
    public static bool CanParse<T>() =>
        typeof(T) == typeof(string) ||
        typeof(T) == typeof(bool) ||
        typeof(T) == typeof(int) ||
        typeof(T) == typeof(long) ||
        typeof(T) == typeof(short) ||
        typeof(T) == typeof(byte) ||
        typeof(T) == typeof(uint) ||
        typeof(T) == typeof(ulong) ||
        typeof(T) == typeof(ushort) ||
        typeof(T) == typeof(sbyte) ||
        typeof(T) == typeof(float) ||
        typeof(T) == typeof(double) ||
        typeof(T) == typeof(decimal) ||
        typeof(T) == typeof(R3.Unit) ||
        BindingRegistry.TryGetEnum<T>(out _);

    public static bool TryParse<T>(string? text, IFormatProvider culture, out T value)
    {
        const NumberStyles integer = NumberStyles.Integer;
        const NumberStyles real = NumberStyles.Float | NumberStyles.AllowThousands;

        if (typeof(T) == typeof(string))
        {
            value = (T)(object)(text ?? "");
            return true;
        }

        if (typeof(T) == typeof(R3.Unit))
        {
            value = default!;
            return true;
        }

        text = text?.Trim();

        if (typeof(T) == typeof(bool))
        {
            return Store(bool.TryParse(text, out bool v), v, out value);
        }

        if (typeof(T) == typeof(int))
        {
            return Store(int.TryParse(text, integer, culture, out int v), v, out value);
        }

        if (typeof(T) == typeof(long))
        {
            return Store(long.TryParse(text, integer, culture, out long v), v, out value);
        }

        if (typeof(T) == typeof(short))
        {
            return Store(short.TryParse(text, integer, culture, out short v), v, out value);
        }

        if (typeof(T) == typeof(byte))
        {
            return Store(byte.TryParse(text, integer, culture, out byte v), v, out value);
        }

        if (typeof(T) == typeof(uint))
        {
            return Store(uint.TryParse(text, integer, culture, out uint v), v, out value);
        }

        if (typeof(T) == typeof(ulong))
        {
            return Store(ulong.TryParse(text, integer, culture, out ulong v), v, out value);
        }

        if (typeof(T) == typeof(ushort))
        {
            return Store(ushort.TryParse(text, integer, culture, out ushort v), v, out value);
        }

        if (typeof(T) == typeof(sbyte))
        {
            return Store(sbyte.TryParse(text, integer, culture, out sbyte v), v, out value);
        }

        if (typeof(T) == typeof(float))
        {
            return Store(float.TryParse(text, real, culture, out float v), v, out value);
        }

        if (typeof(T) == typeof(double))
        {
            return Store(double.TryParse(text, real, culture, out double v), v, out value);
        }

        if (typeof(T) == typeof(decimal))
        {
            return Store(decimal.TryParse(text, real, culture, out decimal v), v, out value);
        }

        if (BindingRegistry.TryGetEnum(out IEnumInfo<T>? info))
        {
            return info.TryParse(text, out value);
        }

        value = default!;
        return false;
    }

    private static bool Store<TParsed, T>(bool parsed, TParsed parsedValue, out T value)
    {
        value = parsed ? Unsafe.As<TParsed, T>(ref parsedValue) : default!;
        return parsed;
    }
}

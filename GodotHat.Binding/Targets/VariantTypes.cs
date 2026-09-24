using System.Runtime.CompilerServices;
using Godot;

namespace GodotHat.Binding;

// Maps binding value types to Godot Variant types. The typeof checks fold away per T when jitted.
internal static class VariantTypes
{
    // The Variant type values of T convert to, or null when T can't be passed to Godot.
    public static Variant.Type? Of<T>() => Cache<T>.Type;

    // Whether a value of Variant type `source` can be assigned to a property of type `target`; Godot converts between
    // numbers and between string types when setting properties.
    public static bool CanAssign(Variant.Type source, Variant.Type target) =>
        source == target ||
        target == Variant.Type.Nil ||
        (IsNumeric(source) && IsNumeric(target)) ||
        (IsText(source) && IsText(target));

    public static bool TryToDouble<T>(T value, out double result)
    {
        if (typeof(T) == typeof(double))
        {
            result = Unsafe.As<T, double>(ref value);
        }
        else if (typeof(T) == typeof(float))
        {
            result = Unsafe.As<T, float>(ref value);
        }
        else if (typeof(T) == typeof(int))
        {
            result = Unsafe.As<T, int>(ref value);
        }
        else if (typeof(T) == typeof(long))
        {
            result = Unsafe.As<T, long>(ref value);
        }
        else
        {
            result = 0;
            return false;
        }

        return true;
    }

    private static bool IsNumeric(Variant.Type type) => type is Variant.Type.Int or Variant.Type.Float or Variant.Type.Bool;

    private static bool IsText(Variant.Type type) => type is Variant.Type.String or Variant.Type.StringName or Variant.Type.NodePath;

    private static class Cache<T>
    {
        public static readonly Variant.Type? Type = Compute();

        private static Variant.Type? Compute()
        {
            Type type = typeof(T);
            if (type == typeof(Variant))
            {
                return Variant.Type.Nil;
            }

            if (type == typeof(R3.Unit) || (Nullable.GetUnderlyingType(type) is not null))
            {
                return null;
            }

            if (typeof(GodotObject).IsAssignableFrom(type))
            {
                return Variant.Type.Object;
            }

            if (type == typeof(string))
            {
                return Variant.Type.String;
            }

            // Anything else Variant.From supports reports its type through a default value.
            try
            {
                using Variant sample = Variant.From(default(T)!);
                return sample.VariantType == Variant.Type.Nil ? null : sample.VariantType;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}

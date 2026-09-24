using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using R3;

namespace GodotHat.Binding;

/// <summary>Ids of the built-in converters.</summary>
public static class BuiltInConverters
{
    /// <summary>Negates a bool; two-way.</summary>
    public const string NotId = "not";

    /// <summary>Alias of <see cref="NotId"/>.</summary>
    public const string InvertId = "invert";

    /// <summary>True when the value is present and not null.</summary>
    public const string NotNullId = "not_null";

    /// <summary>True when the value is missing or null.</summary>
    public const string IsNullId = "is_null";

    /// <summary>Formats the value as text in the current culture; two-way for numbers, bools and view model enums.</summary>
    public const string ToStringId = "to_string";

    /// <summary>Formats the value with the argument as a composite format string, eg <c>Score: {0:N0}</c>.</summary>
    public const string FormatId = "format";

    /// <summary>
    /// True when the value equals the argument (parsed to the value's type); two-way, writing the argument when the
    /// target becomes true, eg for radio buttons.
    /// </summary>
    public const string EqualsId = "equals";

    /// <summary>The name of an enum value; two-way.</summary>
    public const string EnumNameId = "enum_name";

    /// <summary>A collection's item count. Collections bound to a property bind their count; this makes it explicit.</summary>
    public const string CountId = "count";

    /// <summary>True when a collection has any items, or an int count is above zero.</summary>
    public const string AnyId = "any";

    internal sealed class CountConverter(bool any) : IBindingConverter
    {
        public static readonly CountConverter Count = new(false);
        public static readonly CountConverter Any = new(true);

        public bool TryApply<TIn>(
            Observable<BindingValue<TIn>> source,
            string? argument,
            IConverterContinuation<TIn> continuation,
            [NotNullWhen(false)] out string? error)
        {
            if (typeof(TIn) != typeof(int))
            {
                error = $"'{(any ? AnyId : CountId)}' needs a collection or count, not {typeof(TIn).Name}.";
                return false;
            }

            var counts = (Observable<BindingValue<int>>)(object)source;
            if (any)
            {
                continuation.Continue(counts.Select(static v => new BindingValue<bool>(v.HasValue && v.Value > 0)), null);
            }
            else
            {
                continuation.Continue(counts, null);
            }

            error = null;
            return true;
        }
    }

    internal sealed class NotConverter : IBindingConverter
    {
        public static readonly NotConverter Instance = new();

        private static readonly TryConvertBack<bool, bool> Back = static (bool value, out bool result) =>
        {
            result = !value;
            return true;
        };

        public bool TryApply<TIn>(
            Observable<BindingValue<TIn>> source,
            string? argument,
            IConverterContinuation<TIn> continuation,
            [NotNullWhen(false)] out string? error)
        {
            if (typeof(TIn) != typeof(bool))
            {
                error = $"'{NotId}' needs a bool, not {typeof(TIn).Name}.";
                return false;
            }

            var bools = (Observable<BindingValue<bool>>)(object)source;
            continuation.Continue(
                bools.Select(static v => v.HasValue ? new BindingValue<bool>(!v.Value) : v),
                (TryConvertBack<bool, TIn>)(object)Back);
            error = null;
            return true;
        }
    }

    internal sealed class NullConverter(bool whenNull) : IBindingConverter
    {
        public static readonly NullConverter NotNull = new(false);
        public static readonly NullConverter IsNull = new(true);

        public bool TryApply<TIn>(
            Observable<BindingValue<TIn>> source,
            string? argument,
            IConverterContinuation<TIn> continuation,
            [NotNullWhen(false)] out string? error)
        {
            continuation.Continue(
                source.Select(whenNull, static (v, isNull) => new BindingValue<bool>((v.HasValue && v.Value is not null) != isNull)),
                null);
            error = null;
            return true;
        }
    }

    internal sealed class ToStringConverter : IBindingConverter
    {
        public static readonly ToStringConverter Instance = new();

        public bool TryApply<TIn>(
            Observable<BindingValue<TIn>> source,
            string? argument,
            IConverterContinuation<TIn> continuation,
            [NotNullWhen(false)] out string? error)
        {
            Observable<BindingValue<string>> converted = BindingRegistry.TryGetEnum(out IEnumInfo<TIn>? info)
                ? source.Select(info, static (v, names) => v.HasValue ? new BindingValue<string>(names.GetName(v.Value) ?? $"{v.Value}") : BindingValue<string>.Missing)
                : source.Select(static v => v.HasValue ? new BindingValue<string>(v.Value?.ToString() ?? "") : BindingValue<string>.Missing);
            continuation.Continue(converted, ParseBack<TIn>.Instance);
            error = null;
            return true;
        }

        private static class ParseBack<T>
        {
            public static readonly TryConvertBack<string, T>? Instance = ValueParser.CanParse<T>()
                ? static (string text, out T result) => ValueParser.TryParse(text, CultureInfo.CurrentCulture, out result)
                : null;
        }
    }

    internal sealed class FormatConverter : IBindingConverter
    {
        public static readonly FormatConverter Instance = new();

        public bool TryApply<TIn>(
            Observable<BindingValue<TIn>> source,
            string? argument,
            IConverterContinuation<TIn> continuation,
            [NotNullWhen(false)] out string? error)
        {
            CompositeFormat format;
            try
            {
                format = CompositeFormat.Parse(string.IsNullOrEmpty(argument) ? "{0}" : argument);
            }
            catch (FormatException e)
            {
                error = $"'{FormatId}' argument '{argument}' is not a valid format string: {e.Message}";
                return false;
            }

            continuation.Continue(
                source.Select(
                    format,
                    static (v, f) => v.HasValue
                        ? new BindingValue<string>(string.Format(CultureInfo.CurrentCulture, f, v.Value))
                        : BindingValue<string>.Missing),
                null);
            error = null;
            return true;
        }
    }

    internal sealed class EqualsConverter : IBindingConverter
    {
        public static readonly EqualsConverter Instance = new();

        public bool TryApply<TIn>(
            Observable<BindingValue<TIn>> source,
            string? argument,
            IConverterContinuation<TIn> continuation,
            [NotNullWhen(false)] out string? error)
        {
            if (!ValueParser.TryParse(argument, CultureInfo.InvariantCulture, out TIn expected))
            {
                error = $"'{EqualsId}' argument '{argument}' is not a {typeof(TIn).Name}.";
                return false;
            }

            continuation.Continue(
                source.Select(
                    expected,
                    static (v, e) => new BindingValue<bool>(v.HasValue && EqualityComparer<TIn>.Default.Equals(v.Value, e))),
                (bool isEqual, out TIn result) =>
                {
                    result = expected;
                    return isEqual;
                });
            error = null;
            return true;
        }
    }

    internal sealed class EnumNameConverter : IBindingConverter
    {
        public static readonly EnumNameConverter Instance = new();

        public bool TryApply<TIn>(
            Observable<BindingValue<TIn>> source,
            string? argument,
            IConverterContinuation<TIn> continuation,
            [NotNullWhen(false)] out string? error)
        {
            if (!BindingRegistry.TryGetEnum(out IEnumInfo<TIn>? info))
            {
                error = typeof(TIn).IsEnum
                    ? $"'{EnumNameId}' has no table for {typeof(TIn).Name}; enums get one when a [ViewModel] member uses them."
                    : $"'{EnumNameId}' needs an enum, not {typeof(TIn).Name}.";
                return false;
            }

            continuation.Continue(
                source.Select(
                    info,
                    static (v, names) => v.HasValue && names.GetName(v.Value) is { } name
                        ? new BindingValue<string>(name)
                        : BindingValue<string>.Missing),
                info.TryParse);
            error = null;
            return true;
        }
    }
}

using System.Diagnostics.CodeAnalysis;
using R3;

namespace GodotHat.Binding;

/// <summary>Creates converters from delegates.</summary>
public static class BindingConverter
{
    /// <summary>
    /// A converter from <typeparamref name="TIn"/> to <typeparamref name="TOut"/>. Missing values pass through, so
    /// targets show their fallback.
    /// </summary>
    public static IBindingConverter Create<TIn, TOut>(
        Func<TIn, TOut> convert,
        TryConvertBack<TOut, TIn>? convertBack = null) =>
        new DelegateConverter<TIn, TOut>((value, _) => convert(value), convertBack);

    /// <summary>A converter that also receives the binding's converter argument.</summary>
    public static IBindingConverter Create<TIn, TOut>(
        Func<TIn, string?, TOut> convert,
        TryConvertBack<TOut, TIn>? convertBack = null) =>
        new DelegateConverter<TIn, TOut>(convert, convertBack);

    private sealed class DelegateConverter<TIn, TOut>(
        Func<TIn, string?, TOut> convert,
        TryConvertBack<TOut, TIn>? convertBack) : IBindingConverter
    {
        public bool TryApply<TSource>(
            Observable<BindingValue<TSource>> source,
            string? argument,
            IConverterContinuation<TSource> continuation,
            [NotNullWhen(false)] out string? error)
        {
            if (typeof(TSource) != typeof(TIn))
            {
                error = $"Converter takes {typeof(TIn).Name}, not {typeof(TSource).Name}.";
                return false;
            }

            var typedSource = (Observable<BindingValue<TIn>>)(object)source;
            Observable<BindingValue<TOut>> converted = typedSource.Select(
                (convert, argument),
                static (value, state) => value.HasValue
                    ? new BindingValue<TOut>(state.convert(value.Value, state.argument))
                    : BindingValue<TOut>.Missing);

            continuation.Continue(converted, (TryConvertBack<TOut, TSource>?)(object?)convertBack);
            error = null;
            return true;
        }
    }
}

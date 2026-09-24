using System.Diagnostics.CodeAnalysis;
using R3;

namespace GodotHat.Binding;

/// <summary>Converts a target value back to the source type for two-way bindings; false skips the write.</summary>
public delegate bool TryConvertBack<in TOut, TIn>(TOut value, out TIn result);

/// <summary>
/// Receives a converter's output, with its concrete type. See <see cref="IBindingConverter"/>.
/// </summary>
public interface IConverterContinuation<TIn>
{
    /// <summary>Continues building the binding from the converted values.</summary>
    /// <param name="converted">The converted values.</param>
    /// <param name="convertBack">Converts target values back to the source, or null if the conversion is one-way.</param>
    void Continue<TOut>(Observable<BindingValue<TOut>> converted, TryConvertBack<TOut, TIn>? convertBack);
}

/// <summary>
/// A binding converter, applied as an observable operator. Converters are typed at bind time, so values are never
/// boxed: <see cref="TryApply{TIn}"/> receives the source values with their concrete type, and passes its output, with
/// its own concrete type, to the continuation.
/// </summary>
/// <remarks>
/// Most converters are simpler to write with <see cref="BindingConverter.Create{TIn,TOut}(Func{TIn,TOut},TryConvertBack{TOut,TIn})"/>.
/// </remarks>
public interface IBindingConverter
{
    /// <summary>Converts <paramref name="source"/> and passes the result to <paramref name="continuation"/>.</summary>
    /// <param name="source">The source values.</param>
    /// <param name="argument">The binding's converter argument, eg a format string.</param>
    /// <param name="continuation">Receives the converted values.</param>
    /// <param name="error">Why the converter can't be applied, eg an unsupported source type.</param>
    /// <returns>Whether the continuation was called.</returns>
    bool TryApply<TIn>(
        Observable<BindingValue<TIn>> source,
        string? argument,
        IConverterContinuation<TIn> continuation,
        [NotNullWhen(false)] out string? error);
}

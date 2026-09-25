namespace GodotHat.Binding;

/// <summary>A problem resolving, converting or applying a binding.</summary>
/// <param name="Target">Where the binding is, eg a node path and property.</param>
/// <param name="Path">The binding's source path.</param>
/// <param name="Message">What went wrong.</param>
/// <param name="Exception">The exception, if one was thrown.</param>
public readonly record struct BindingError(string Target, string Path, string Message, Exception? Exception = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        this.Exception is null
            ? $"Binding '{this.Path}' on {this.Target}: {this.Message}"
            : $"Binding '{this.Path}' on {this.Target}: {this.Message} ({this.Exception.GetType().Name}: {this.Exception.Message})";
}

/// <summary>Receives binding errors. Implementations must be thread-safe; sources can fail on any thread.</summary>
public interface IBindingErrorSink
{
    /// <summary>Reports an error.</summary>
    void Report(in BindingError error);
}

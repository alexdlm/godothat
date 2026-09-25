using System.Diagnostics.CodeAnalysis;
using Godot;
using R3;

namespace GodotHat.Binding;

// A signal that executes a command. Availability disables a BaseButton, or sets a `disabled` property if there is one.
internal sealed class SignalCommandTarget(Node node, string signal, IBindingErrorSink errors, string path) : ICommandTarget
{
    private static readonly StringName Disabled = "disabled";
    private string? description;

    public string Description => this.description ??= $"{node.GetPath()}:{signal}";

    public IDisposable ObserveInvoked(Action onInvoked)
    {
        if (SignalConnection.TryConnect(node, signal, onInvoked, out SignalConnection? connection, out string? error))
        {
            return connection!;
        }

        errors.Report(new BindingError(this.Description, path, error!));
        return Disposable.Empty;
    }

    public void SetAvailable(bool available)
    {
        if (node is BaseButton button)
        {
            button.Disabled = !available;
        }
        else if (PropertyInfoCache.TryGetProperty(node, "disabled", out PropertyMeta meta) && meta.Type == Variant.Type.Bool)
        {
            node.Set(Disabled, !available);
        }
    }
}

// A method called for each value of a view model observable.
internal sealed class MethodEventTarget(Node node, string method) : IEventTarget
{
    private readonly StringName methodName = method;
    private string? description;

    public string Description => this.description ??= $"{node.GetPath()}:{method}()";

    public bool CanAccept<T>([NotNullWhen(false)] out string? error)
    {
        if (!node.HasMethod(this.methodName))
        {
            error = $"{node.GetClass()} has no method '{method}'.";
            return false;
        }

        if (typeof(T) != typeof(Unit) && VariantTypes.Of<T>() is null)
        {
            error = $"{typeof(T).Name} values can't be passed to Godot; add a converter such as to_string or format.";
            return false;
        }

        error = null;
        return true;
    }

    public void Invoke<[MustBeVariant] T>(T payload)
    {
        if (typeof(T) == typeof(Unit))
        {
            node.Call(this.methodName);
            return;
        }

        using Variant argument = Variant.From(payload);
        node.Call(this.methodName, argument);
    }
}

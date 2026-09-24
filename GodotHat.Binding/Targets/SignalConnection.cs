using Godot;

namespace GodotHat.Binding;

// A connection to a Godot signal that ignores its arguments, disconnected on dispose. The callable must take exactly
// the signal's argument count, so the arguments are taken as Variants, which need no conversion. Each is a copy, freed
// here rather than by the finalizer thread.
internal sealed class SignalConnection : IDisposable
{
    private readonly GodotObject source;
    private readonly StringName signal;
    private Callable callable;
    private bool connected;

    private SignalConnection(GodotObject source, StringName signal, Callable callable)
    {
        this.source = source;
        this.signal = signal;
        this.callable = callable;
        this.connected = source.Connect(signal, callable) == Error.Ok;
    }

    public static bool TryConnect(GodotObject source, string signal, Action handler, out SignalConnection? connection, out string? error)
    {
        connection = null;
        if (!PropertyInfoCache.TryGetSignalArgumentCount(source, signal, out int argumentCount))
        {
            error = $"{source.GetClass()} has no signal '{signal}'.";
            return false;
        }

        Callable callable = argumentCount switch
        {
            0 => Callable.From(handler),
            1 => Callable.From((Variant a) =>
            {
                a.Dispose();
                handler();
            }),
            2 => Callable.From((Variant a, Variant b) =>
            {
                a.Dispose();
                b.Dispose();
                handler();
            }),
            3 => Callable.From((Variant a, Variant b, Variant c) =>
            {
                a.Dispose();
                b.Dispose();
                c.Dispose();
                handler();
            }),
            4 => Callable.From((Variant a, Variant b, Variant c, Variant d) =>
            {
                a.Dispose();
                b.Dispose();
                c.Dispose();
                d.Dispose();
                handler();
            }),
            _ => default,
        };

        if (argumentCount > 4)
        {
            error = $"Signal '{signal}' has more than 4 arguments.";
            return false;
        }

        connection = new SignalConnection(source, signal, callable);
        error = connection.connected ? null : $"Failed to connect to signal '{signal}'.";
        return connection.connected;
    }

    public void Dispose()
    {
        if (!this.connected)
        {
            return;
        }

        this.connected = false;
        if (GodotObject.IsInstanceValid(this.source) && this.source.IsConnected(this.signal, this.callable))
        {
            this.source.Disconnect(this.signal, this.callable);
        }

        this.callable = default;
    }
}

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using R3;

namespace GodotHat.Binding.Core.Test.Support;

public sealed class ErrorCollector : IBindingErrorSink
{
    public ConcurrentQueue<BindingError> Errors { get; } = new();

    public void Report(in BindingError error) => this.Errors.Enqueue(error);
}

// Runs posted work when told to, like Godot's once-per-frame pump.
public sealed class ManualSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> work = new();

    public int Pending => this.work.Count;

    public override void Post(SendOrPostCallback d, object? state) => this.work.Enqueue((d, state));

    public void RunFrame()
    {
        int count = this.work.Count;
        for (int i = 0; i < count && this.work.TryDequeue(out var item); i++)
        {
            item.Callback(item.State);
        }
    }
}

public sealed class FakeTarget(string description = "target") : IBindingTarget
{
    private Action? onChanged;

    public string Description => description;
    public Type? RejectType { get; init; }
    public bool HasChangeNotification { get; init; } = true;
    public object? Value { get; private set; } = "<initial>";
    public List<object?> Applied { get; } = [];
    public int FallbackCount { get; private set; }
    public int ThreadId { get; private set; }

    public bool CanAccept<T>([NotNullWhen(false)] out string? error)
    {
        error = typeof(T) == this.RejectType ? $"Rejects {typeof(T).Name}" : null;
        return error is null;
    }

    public void SetValue<T>(T value)
    {
        this.Value = value;
        this.Applied.Add(value);
        this.ThreadId = Environment.CurrentManagedThreadId;
    }

    public void SetFallback()
    {
        this.Value = "<fallback>";
        this.FallbackCount++;
    }

    public bool TryReadValue<T>(out T value)
    {
        if (this.Value is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public IDisposable? ObserveChanges(Action onChanged)
    {
        if (!this.HasChangeNotification)
        {
            return null;
        }

        this.onChanged = onChanged;
        return Disposable.Create(() => this.onChanged = null);
    }

    // Simulates the user editing the target.
    public void UserSets(object? value)
    {
        this.Value = value;
        this.onChanged?.Invoke();
    }
}

// Stores values of one type without boxing, for allocation tests.
public sealed class TypedTarget<TValue> : IBindingTarget
{
    public TValue Value = default!;
    public int Count;

    public string Description => "typed";

    public bool CanAccept<T>([NotNullWhen(false)] out string? error)
    {
        error = typeof(T) == typeof(TValue) ? null : "wrong type";
        return error is null;
    }

    public void SetValue<T>(T value)
    {
        this.Value = Unsafe.As<T, TValue>(ref value);
        this.Count++;
    }

    public void SetFallback()
    {
    }

    public bool TryReadValue<T>(out T value)
    {
        value = default!;
        return false;
    }

    public IDisposable? ObserveChanges(Action onChanged) => null;
}

public sealed class FakeCommandTarget : ICommandTarget
{
    private Action? onInvoked;

    public string Description => "button.pressed";
    public bool? Available { get; private set; }
    public bool IsObserved => this.onInvoked is not null;

    public IDisposable ObserveInvoked(Action onInvoked)
    {
        this.onInvoked = onInvoked;
        return Disposable.Create(() => this.onInvoked = null);
    }

    public void SetAvailable(bool available) => this.Available = available;

    public void Press() => this.onInvoked?.Invoke();
}

public sealed class FakeEventTarget : IEventTarget
{
    public string Description => "node.method";
    public List<object?> Calls { get; } = [];

    public bool CanAccept<T>([NotNullWhen(false)] out string? error)
    {
        error = null;
        return true;
    }

    public void Invoke<T>(T payload) => this.Calls.Add(payload);
}

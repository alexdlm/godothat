using Godot;

namespace GodotHat.Binding;

/// <summary>Global configuration for GodotHat.Binding in Godot.</summary>
/// <example>
/// Set services up before the first scene with bindings loads, eg in an autoload's constructor:
/// <code>
/// GodotBinding.Activator = new GameViewModelActivator(services);
/// </code>
/// </example>
public static class GodotBinding
{
    private static BindingEngine? engine;

    /// <summary>Creates view models for <see cref="BindingRootBase.ViewModelType"/>.</summary>
    public static IViewModelActivator Activator { get; set; } = DefaultViewModelActivator.Instance;

    /// <summary>Converters available to bindings.</summary>
    public static BindingConverterRegistry Converters { get; set; } = BindingConverterRegistry.Default;

    /// <summary>Raised for every binding error, on the thread it occurred on, after it is logged with GD.PushError.</summary>
    public static event Action<BindingError>? ErrorReported;

    /// <summary>
    /// The engine used by binding roots. Created on first use, which must be on the main thread; updates from other
    /// threads are applied on the main thread through Godot's <see cref="Dispatcher.SynchronizationContext"/>.
    /// </summary>
    public static BindingEngine Engine => engine ??= CreateEngine();

    private static BindingEngine CreateEngine()
    {
        // The dispatcher treats the creating thread as the main thread
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
        {
            throw new InvalidOperationException("GodotBinding.Engine must first be used on the main thread.");
        }

        return new BindingEngine(
            new BindingDispatcher(Dispatcher.SynchronizationContext, System.Environment.CurrentManagedThreadId),
            ErrorSink.Instance,
            Converters);
    }

    private sealed class ErrorSink : IBindingErrorSink
    {
        public static readonly ErrorSink Instance = new();

        public void Report(in BindingError error)
        {
            GD.PushError(error.ToString());
            ErrorReported?.Invoke(error);
        }
    }
}

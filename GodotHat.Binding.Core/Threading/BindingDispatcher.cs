namespace GodotHat.Binding;

/// <summary>
/// Delivers binding updates on the main thread. Updates that already arrive on the main thread apply immediately;
/// updates from other threads, and coalesced updates, are posted to a <see cref="SynchronizationContext"/> that runs
/// them on the main thread once per frame, such as Godot's <c>Dispatcher.SynchronizationContext</c>.
/// </summary>
public sealed class BindingDispatcher
{
    private static readonly SendOrPostCallback RunWork = static state => ((IDispatchWork)state!).Run();

    private readonly SynchronizationContext? context;
    private readonly int mainThreadId;

    /// <summary>Creates a dispatcher that posts to <paramref name="context"/>.</summary>
    /// <param name="context">Runs posted work on the main thread.</param>
    /// <param name="mainThreadId">The main thread's <see cref="Environment.CurrentManagedThreadId"/>.</param>
    public BindingDispatcher(SynchronizationContext context, int mainThreadId)
    {
        this.context = context;
        this.mainThreadId = mainThreadId;
    }

    private BindingDispatcher()
    {
    }

    /// <summary>
    /// Applies every update immediately on the calling thread, including coalesced ones. For single-threaded use and
    /// tests.
    /// </summary>
    public static BindingDispatcher Immediate { get; } = new();

    /// <summary>Whether the calling thread is the main thread.</summary>
    public bool IsMainThread => this.context is null || Environment.CurrentManagedThreadId == this.mainThreadId;

    internal void Post(IDispatchWork work)
    {
        if (this.context is null)
        {
            work.Run();
        }
        else
        {
            this.context.Post(RunWork, work);
        }
    }
}

internal interface IDispatchWork
{
    void Run();
}

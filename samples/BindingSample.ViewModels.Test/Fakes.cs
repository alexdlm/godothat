namespace BindingSample.ViewModels.Test;

using System.Collections.Concurrent;
using GodotHat.Binding;

public sealed class FakeSettingsStore : ISettingsStore
{
    private readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<string> Saved { get; } = new();

    // Completes saves only when the test says so, to check the page while a save is in progress.
    public bool Hold { get; init; }

    public Exception? Failure { get; init; }

    public Task SaveAsync(string settings)
    {
        if (this.Failure is not null)
        {
            return Task.FromException(this.Failure);
        }

        this.Saved.Enqueue(settings);
        return this.Hold ? this.done.Task : Task.CompletedTask;
    }

    public void Complete() => this.done.SetResult();
}

// Creates pages as the game's activator would, with fakes for their services.
public sealed class FakeActivator(TimeProvider time) : IViewModelActivator
{
    public object Create(Type viewModelType) =>
        viewModelType == typeof(SettingsViewModel)
            ? new SettingsViewModel(new FakeSettingsStore(), time)
            : Activator.CreateInstance(viewModelType)!;
}

namespace BindingSample.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BindingSample.ViewModels;
using GodotHat.Binding;
using Jab;

public sealed class FakeSettingsStore : ISettingsStore
{
    private readonly List<string> saved = new();

    public IReadOnlyList<string> Saved
    {
        get
        {
            lock (this.saved)
            {
                return this.saved.ToArray();
            }
        }
    }

    // Called on a worker thread
    public Task SaveAsync(string settings)
    {
        lock (this.saved)
        {
            this.saved.Add(settings);
        }

        return Task.CompletedTask;
    }
}

// The game's services with fakes, for mounting whole scenes in tests.
[ServiceProvider]
[Singleton(typeof(ISettingsStore), typeof(FakeSettingsStore))]
[Singleton(typeof(TimeProvider), Factory = nameof(SystemTime))]
[Singleton(typeof(IViewModelActivator), Factory = nameof(CreateActivator))]
public partial class TestServices
{
    private static TimeProvider SystemTime() => TimeProvider.System;

    private IViewModelActivator CreateActivator() => new ServiceProviderViewModelActivator(this);
}

namespace BindingSample.Services;

using System;
using System.Threading.Tasks;
using BindingSample.ViewModels;
using GodotHat.Binding;
using Jab;

// Pretends to write settings somewhere slow.
public sealed class SettingsStore : ISettingsStore
{
    public async Task SaveAsync(string settings) => await Task.Delay(TimeSpan.FromMilliseconds(50));
}

// Services come from Jab. View models aren't registered here: the activator creates them with their constructor's
// dependencies from this provider, and whoever creates a view model disposes it.
[ServiceProvider]
[Singleton(typeof(ISettingsStore), typeof(SettingsStore))]
[Singleton(typeof(TimeProvider), Factory = nameof(SystemTime))]
[Singleton(typeof(IViewModelActivator), Factory = nameof(CreateActivator))]
public partial class SampleServices
{
    private static TimeProvider SystemTime() => TimeProvider.System;

    private IViewModelActivator CreateActivator() => new ServiceProviderViewModelActivator(this);
}

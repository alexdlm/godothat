namespace BindingSample.ViewModels;

using GodotHat.Binding;
using R3;

[ViewModel]
public sealed class AboutViewModel : ViewModelBase
{
    public string Text => "GodotHat.Binding sample: pages are view models, shown by a ContentPresenter.";
}

// Navigation between pages. The presenter showing Page owns the pages, so it disposes each one it replaces.
[ViewModel]
public sealed class ShellViewModel : ViewModelBase
{
    public ShellViewModel(IViewModelActivator activator)
    {
        this.Page = new ReactiveProperty<object?>().AddTo(this.Bag);
        this.ShowSettings = this.Page.Select(page => page is not SettingsViewModel)
            .ToReactiveCommand(_ => this.Page.Value = activator.Create(typeof(SettingsViewModel)))
            .AddTo(this.Bag);
        this.ShowAbout = this.Page.Select(page => page is not AboutViewModel)
            .ToReactiveCommand(_ => this.Page.Value = new AboutViewModel())
            .AddTo(this.Bag);
        this.Page.Value = activator.Create(typeof(SettingsViewModel));
    }

    public ReactiveProperty<object?> Page { get; }
    public ReactiveCommand ShowSettings { get; }
    public ReactiveCommand ShowAbout { get; }
}

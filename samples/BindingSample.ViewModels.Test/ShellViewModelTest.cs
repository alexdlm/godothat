namespace BindingSample.ViewModels.Test;

using Microsoft.Extensions.Time.Testing;
using R3;

public class ShellViewModelTest
{
    [Fact]
    public void StartsOnTheSettingsPage()
    {
        using var shell = new ShellViewModel(new FakeActivator(new FakeTimeProvider()));

        Assert.IsType<SettingsViewModel>(shell.Page.Value);
        Assert.False(shell.ShowSettings.CanExecute());
        Assert.True(shell.ShowAbout.CanExecute());
    }

    [Fact]
    public void NavigatesBetweenPages()
    {
        using var shell = new ShellViewModel(new FakeActivator(new FakeTimeProvider()));
        object settings = shell.Page.Value!;

        shell.ShowAbout.Execute(Unit.Default);
        Assert.IsType<AboutViewModel>(shell.Page.Value);
        Assert.True(shell.ShowSettings.CanExecute());

        shell.ShowSettings.Execute(Unit.Default);
        Assert.IsType<SettingsViewModel>(shell.Page.Value);
        Assert.NotSame(settings, shell.Page.Value);
    }
}

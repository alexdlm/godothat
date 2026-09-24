namespace BindingSample.ViewModels.Test;

using Microsoft.Extensions.Time.Testing;
using R3;

public class SettingsViewModelTest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void SummarisesTheSettings()
    {
        using var vm = new SettingsViewModel(new FakeSettingsStore(), new FakeTimeProvider());

        vm.PlayerName.Value = "Grace";
        vm.Difficulty.Value = Difficulty.Brutal;

        Assert.Equal("Grace: Brutal, volume 70%", vm.Summary.CurrentValue);
    }

    [Fact]
    public void SaveIsAvailableOnceSomethingChanges()
    {
        using var vm = new SettingsViewModel(new FakeSettingsStore(), new FakeTimeProvider());
        Assert.False(vm.Save.CanExecute());

        vm.Fullscreen.Value = true;

        Assert.True(vm.IsDirty.Value);
        Assert.True(vm.Save.CanExecute());
    }

    [Fact]
    public async Task SavingRecordsHistoryAndReportsStatus()
    {
        var store = new FakeSettingsStore();
        using var vm = new SettingsViewModel(store, new FakeTimeProvider());
        vm.PlayerName.Value = "Grace";
        Task<string> status = vm.Status.FirstAsync();

        vm.Save.Execute(Unit.Default);

        // Saving finishes on another thread
        Assert.Equal("Saved 1 time(s)", await status.WaitAsync(Timeout));
        Assert.Equal(["Grace: Normal, volume 70%"], store.Saved);
        Assert.Equal(new SaveEntry(1, "Grace: Normal, volume 70%"), Assert.Single(vm.History));
        Assert.False(vm.IsDirty.Value);
        Assert.False(vm.Save.CanExecute());
    }

    [Fact]
    public async Task StatusClearsAfterAWhile()
    {
        var time = new FakeTimeProvider();
        using var vm = new SettingsViewModel(new FakeSettingsStore(), time);
        vm.PlayerName.Value = "Grace";
        Task<string[]> messages = vm.Status.Take(2).ToArrayAsync();
        Task<string> saved = vm.Status.FirstAsync();

        vm.Save.Execute(Unit.Default);
        await saved.WaitAsync(Timeout);
        time.Advance(SettingsViewModel.StatusDuration - TimeSpan.FromMilliseconds(1));
        Assert.False(messages.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(["Saved 1 time(s)", ""], await messages.WaitAsync(Timeout));
    }

    [Fact]
    public async Task SaveIsUnavailableWhileSaving()
    {
        var store = new FakeSettingsStore { Hold = true };
        using var vm = new SettingsViewModel(store, new FakeTimeProvider());
        vm.PlayerName.Value = "Grace";
        Task<string> status = vm.Status.FirstAsync();

        vm.Save.Execute(Unit.Default);
        Assert.True(vm.IsSaving.CurrentValue);
        Assert.False(vm.Save.CanExecute());

        store.Complete();
        await status.WaitAsync(Timeout);
        Assert.False(vm.IsSaving.CurrentValue);
    }

    [Fact]
    public async Task ReportsFailedSaves()
    {
        using var vm = new SettingsViewModel(new FakeSettingsStore { Failure = new IOException("disk full") }, new FakeTimeProvider());
        vm.PlayerName.Value = "Grace";
        Task<string> status = vm.Status.FirstAsync();

        vm.Save.Execute(Unit.Default);

        Assert.Equal("Couldn't save: disk full", await status.WaitAsync(Timeout));
        Assert.Empty(vm.History);
        Assert.True(vm.Save.CanExecute(), "the settings are still unsaved");
    }

    [Fact]
    public void ResetNameRestoresTheDefault()
    {
        using var vm = new SettingsViewModel(new FakeSettingsStore(), new FakeTimeProvider());
        vm.PlayerName.Value = "Grace";

        vm.ResetName.Execute(Unit.Default);

        Assert.Equal("Ada", vm.PlayerName.Value);
    }
}

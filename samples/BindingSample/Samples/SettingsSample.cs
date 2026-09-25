namespace BindingSample.Samples;

using System.Threading.Tasks;
using BindingSample.ViewModels;
using Godot;
using GodotHat.Binding;

// Sample data for Settings.tscn: what the editor's preview shows, and the starting state for tests that mount the
// page with it.
[GlobalClass]
public partial class SettingsSample : Resource, IViewModelSource
{
    [Export]
    public string PlayerName { get; set; } = "Sam";

    [Export]
    public Difficulty Difficulty { get; set; } = Difficulty.Brutal;

    [Export]
    public int PastSaves { get; set; } = 2;

    public object CreateViewModel()
    {
        var vm = new SettingsViewModel(new NoStore(), System.TimeProvider.System);
        vm.PlayerName.Value = this.PlayerName;
        vm.Difficulty.Value = this.Difficulty;
        for (int i = 1; i <= this.PastSaves; i++)
        {
            vm.History.Insert(0, new SaveEntry(i, $"{this.PlayerName}: save {i}"));
        }

        vm.Saves.Value = this.PastSaves;
        vm.IsDirty.Value = false;
        return vm;
    }

    private sealed class NoStore : ISettingsStore
    {
        public Task SaveAsync(string settings) => Task.CompletedTask;
    }
}

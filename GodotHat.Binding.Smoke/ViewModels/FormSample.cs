namespace GodotHat.Binding.Smoke.ViewModels;

using Godot;

// Sample data for previews and standalone runs.
[GlobalClass]
public partial class FormSample : Resource, IViewModelSource
{
    [Export]
    public string Name { get; set; } = "Sample";

    public object CreateViewModel()
    {
        var vm = new FormViewModel();
        vm.Name.Value = this.Name;
        return vm;
    }
}

namespace BindingSample.Services;

using Godot;
using GodotHat.Binding;

// An autoload, so it is set up before the main scene's binding roots create their view models.
public partial class ServicesAutoload : Node
{
    private readonly SampleServices services = new();

    public override void _EnterTree() => GodotBinding.Activator = this.services.GetService<IViewModelActivator>();

    public override void _ExitTree() => this.services.Dispose();
}

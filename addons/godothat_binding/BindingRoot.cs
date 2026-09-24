namespace GodotHat.Binding;

using Godot;

/// <summary>
/// Binds the Controls below it to a view model, from the bindings in each Control's metadata. All behaviour is in
/// <see cref="BindingRootBase"/>; derive from that, or from this, to customise it.
/// </summary>
[Tool]
[GlobalClass]
public partial class BindingRoot : BindingRootBase
{
}

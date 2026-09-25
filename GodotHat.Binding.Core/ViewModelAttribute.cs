namespace GodotHat.Binding;

/// <summary>
/// Marks a class as a view model, so GodotHat.Binding's generator emits a typed accessor for its bindable members.
/// </summary>
/// <remarks>
/// The generator never adds members to the class, so it need not be <c>partial</c> and can derive from anything,
/// including Godot nodes. Subclasses need their own <c>[ViewModel]</c> for their new members to be bindable.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ViewModelAttribute : Attribute
{
}

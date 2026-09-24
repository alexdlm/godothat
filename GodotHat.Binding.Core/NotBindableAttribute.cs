namespace GodotHat.Binding;

/// <summary>
/// Excludes a member from binding. On a view model member it hides that member from bindings. On a Control's
/// property it hides that property as a binding target, and on a Control class it hides all the properties the class
/// declares.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class, Inherited = false)]
public sealed class NotBindableAttribute : Attribute
{
}

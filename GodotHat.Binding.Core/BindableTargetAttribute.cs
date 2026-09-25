namespace GodotHat.Binding;

/// <summary>
/// Offers a property of a custom Control as a binding target even though it isn't exported. Exported properties are
/// bindable by default.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class BindableTargetAttribute : Attribute
{
}

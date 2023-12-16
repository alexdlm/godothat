namespace GodotHat;

/// <summary>
/// Do not expose method to Godot via GodotHat's ScriptMethods generator.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class GodotIgnoreAttribute : Attribute
{
}

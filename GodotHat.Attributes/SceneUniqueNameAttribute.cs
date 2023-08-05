namespace GodotHat;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SceneUniqueNameAttribute : Attribute
{
    /**
     * The node path to resolve, typically a unique name from the scene, eg "%Foo".
     * If empty, this will resolve to the unique name of the field/property this attribute is on, with the "%" prefixed.
     */
    public string? NodePath { get; }
    public bool Required { get; }

    public SceneUniqueNameAttribute(string? nodePath = null, bool required = true)
    {
        this.NodePath = nodePath;
        this.Required = required;
    }
}

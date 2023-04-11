namespace GodotHat;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SceneUniqueNameAttribute : Attribute
{
    public string SceneUniqueName { get; }
    public bool Required { get; }

    public SceneUniqueNameAttribute(string sceneUniqueName, bool required = true)
    {
        this.SceneUniqueName = sceneUniqueName;
        this.Required = required;
    }
}

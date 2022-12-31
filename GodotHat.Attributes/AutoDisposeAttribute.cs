namespace GodotHat;

/// <summary>
/// Annotate a private method that returns an IDisposable?. Generates a public Setter and tracks disposable, dispose
/// is called on ExitTree.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AutoDisposeAttribute : Attribute
{
    public AutoDisposeAttribute(Accessibility accessibility = Accessibility.Public)
    {
        this.Accessibility = accessibility;
    }

    public Accessibility Accessibility { get; init; }
}

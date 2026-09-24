namespace GodotHat.Binding.Testing;

/// <summary>Test assertions over <see cref="BindingValidator"/>, which checks bindings in scenes without running them.</summary>
/// <example>
/// A test that fails on any broken binding in the project, eg after renaming a view model member:
/// <code>
/// [TestCase]
/// public void BindingsAreValid() => BindingValidation.AssertProjectValid();
/// </code>
/// </example>
public static class BindingValidation
{
    /// <summary>Fails if any binding in a scene under <paramref name="directory"/> is broken.</summary>
    /// <param name="directory">Where to look for scenes; addons are skipped.</param>
    /// <param name="warningsAsErrors">Whether bindings that work but probably not as intended also fail.</param>
    public static void AssertProjectValid(string directory = "res://", bool warningsAsErrors = false) =>
        AssertValid(BindingValidator.ValidateProject(directory), warningsAsErrors);

    /// <summary>Fails if any binding in the scene at <paramref name="scenePath"/> is broken.</summary>
    /// <param name="scenePath">The scene's resource path.</param>
    /// <param name="warningsAsErrors">Whether bindings that work but probably not as intended also fail.</param>
    public static void AssertSceneValid(string scenePath, bool warningsAsErrors = false) =>
        AssertValid(BindingValidator.ValidateScene(scenePath), warningsAsErrors);

    /// <summary>Fails if any of <paramref name="reports"/> is an error, or a warning when <paramref name="warningsAsErrors"/>.</summary>
    public static void AssertValid(IEnumerable<BindingReport> reports, bool warningsAsErrors = false)
    {
        List<BindingReport> problems = reports
            .Where(r => r.Status == BindingStatus.Error || (warningsAsErrors && r.Status == BindingStatus.Warning))
            .ToList();
        if (problems.Count > 0)
        {
            throw new BindingAssertionException(
                $"{problems.Count} broken binding(s):{string.Concat(problems.Select(p => $"{System.Environment.NewLine}  {p}"))}");
        }
    }
}

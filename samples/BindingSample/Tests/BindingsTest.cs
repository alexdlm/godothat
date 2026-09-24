namespace BindingSample.Tests;

using GdUnit4;
using GodotHat.Binding.Testing;

[TestSuite]
[RequireGodotRuntime]
public class BindingsTest
{
    // Fails when a view model change, such as renaming a member, breaks a binding in any scene.
    [TestCase]
    public void AllBindingsAreValid() => BindingValidation.AssertProjectValid();
}

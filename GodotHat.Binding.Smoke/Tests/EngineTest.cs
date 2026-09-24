namespace GodotHat.Binding.Smoke.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class EngineTest
{
    [TestCase]
    public void RunsInsideGodot()
    {
        AssertThat(Engine.GetMainLoop() is SceneTree).IsTrue();
    }
}

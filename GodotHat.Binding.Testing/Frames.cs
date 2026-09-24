using Godot;

namespace GodotHat.Binding.Testing;

internal static class Frames
{
    public static SceneTree Tree =>
        Engine.GetMainLoop() as SceneTree ?? throw new InvalidOperationException("GodotHat.Binding.Testing needs a running Godot SceneTree.");

    public static async Task Next(int count = 1)
    {
        SceneTree tree = Tree;
        for (int i = 0; i < count; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }
}

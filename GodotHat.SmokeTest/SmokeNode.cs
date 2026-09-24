namespace GodotHat.SmokeTest;

using System;
using Godot;
using Godot.Collections;

public partial class SmokeNode : Node
{
    [SceneUniqueName(required: false)]
    private Label? Label { get; set; }

    [OnReady]
    private IDisposable? Subscribe() => null;

    [OnEnterTree]
    private void EnterTree() { }

    [OnExitTree]
    private void ExitTree() { }

    [AutoDispose]
    private IDisposable Track(Node node) => new System.IO.MemoryStream();

    public int CountNodes(Array<Node> nodes) => nodes.Count;

    public Node[] GetNodes() => [];

    // References members that only exist if GodotHat's generators ran
    private void UsesGeneratedMembers()
    {
        this.UpdateTrack(this);
        _ = MethodName.CountNodes;
        _ = MethodName._Ready;
    }

    public partial class Nested : Node
    {
        [OnReady]
        private void NestedReady() { }
    }
}

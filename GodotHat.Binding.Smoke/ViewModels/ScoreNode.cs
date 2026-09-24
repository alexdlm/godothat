namespace GodotHat.Binding.Smoke.ViewModels;

using Godot;
using R3;

// A node that is its own view model.
[ViewModel]
public partial class ScoreNode : Node
{
    public ReactiveProperty<int> Score { get; } = new(5);

    // Disposing reactive members with the node is the recommended pattern; bindings also let go when a node leaves
    // the tree without doing so.
    public bool DisposeOnExit { get; set; }

    public override void _ExitTree()
    {
        if (this.DisposeOnExit)
        {
            this.Score.Dispose();
        }
    }
}

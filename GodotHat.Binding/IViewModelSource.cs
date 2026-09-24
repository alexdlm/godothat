namespace GodotHat.Binding;

/// <summary>
/// Creates a view model for a binding root that isn't given one, typically sample data on a Resource so a component
/// scene can be run on its own (F6) or loaded in tests.
/// </summary>
/// <example>
/// <code>
/// [GlobalClass]
/// public partial class QueueScreenSample : Resource, IViewModelSource
/// {
///     [Export] public int Orders { get; set; } = 3;
///
///     public object CreateViewModel() => new QueueScreenViewModel(new FakeBuildQueue(Orders));
/// }
/// </code>
/// </example>
public interface IViewModelSource
{
    /// <summary>Creates the view model. The binding root disposes it, if it is disposable, when it leaves the tree.</summary>
    object CreateViewModel();
}

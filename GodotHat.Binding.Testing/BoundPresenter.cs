using Godot;

namespace GodotHat.Binding.Testing;

/// <summary>A ContentPresenter, whose current content is the scope to find Controls in.</summary>
public sealed class BoundPresenter : BindingScope
{
    private readonly string description;

    internal BoundPresenter(ContentPresenterBase presenter, string description)
    {
        this.Control = presenter;
        this.description = description;
    }

    /// <summary>The presenter.</summary>
    public ContentPresenterBase Control { get; }

    /// <summary>The scene it shows, if any.</summary>
    public Node? Content => this.Control.Content;

    /// <summary>The view model it shows, if any.</summary>
    public object? ViewModel => this.Control.ContentViewModel;

    /// <summary>The scene it shows.</summary>
    /// <exception cref="BindingAssertionException">It shows nothing.</exception>
    public override Node Node => this.Content ?? throw new BindingAssertionException($"{this.description} shows nothing.");

    /// <summary>Checks that it shows a view model of type <typeparamref name="TViewModel"/>, and returns it.</summary>
    public TViewModel AssertShowing<TViewModel>() =>
        this.ViewModel is TViewModel viewModel
            ? viewModel
            : throw new BindingAssertionException(
                $"{this.description} expected to show a {typeof(TViewModel).Name}, but shows {this.ViewModel?.GetType().Name ?? "nothing"}.");

    /// <summary>Checks the resource path of the scene it shows, eg <c>res://ui/settings.tscn</c>.</summary>
    public BoundPresenter AssertScene(string scenePath)
    {
        string? actual = this.Content?.SceneFilePath;
        return actual == scenePath
            ? this
            : throw new BindingAssertionException(
                $"{this.description} expected to show {scenePath}, but shows {(actual is null ? "nothing" : actual.Length == 0 ? "a scene built in code" : actual)}.");
    }

    /// <summary>Checks that it shows nothing.</summary>
    public BoundPresenter AssertEmpty() =>
        this.Content is null
            ? this
            : throw new BindingAssertionException($"{this.description} expected to show nothing, but shows {this.ViewModel?.GetType().Name}.");
}

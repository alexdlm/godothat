using Godot;
using R3;

namespace GodotHat.Binding;

/// <summary>
/// Shows the scene for the view model in its <c>@context</c> binding, replacing it when the view model changes: for
/// pages, tabs and panels. Scenes use the <c>ContentPresenter</c> node from the <c>godothat_binding</c> addon, which
/// derives from this.
/// </summary>
/// <remarks>
/// The scene comes from <see cref="Views"/>, then <see cref="ViewRegistry"/>, then <see cref="Template"/>. A scene rooted at
/// a binding root is given the view model as its <see cref="BindingRootBase.Context"/>; any other scene is bound with the
/// view model as its data context.
/// </remarks>
public partial class ContentPresenterBase : Control
{
    private BindingRootBase? root;
    private IDisposable? subscription;

    /// <summary>Scenes by the full name of the view model type they present.</summary>
    [Export]
    public Godot.Collections.Dictionary<string, PackedScene> Views { get; set; } = new();

    /// <summary>The scene for view models not found in <see cref="Views"/> or <see cref="ViewRegistry"/>.</summary>
    [Export]
    public PackedScene? Template { get; set; }

    /// <summary>
    /// Dispose view models when they're replaced or the presenter is unbound, for pages the presenter's owner creates
    /// for it. Leave unset for view models that live on, eg tabs switched back and forth.
    /// </summary>
    [Export]
    public bool OwnsContent { get; set; }

    /// <summary>The scene currently shown.</summary>
    public Node? Content { get; private set; }

    /// <summary>The view model currently shown.</summary>
    public object? ContentViewModel { get; private set; }

    // Called by the binding root with the context from the presenter's @context binding.
    internal IDisposable Present(BindingRootBase bindingRoot, DataContext context)
    {
        this.root = bindingRoot;
        this.subscription = context.Values.Subscribe((this, context), static (value, state) => state.Item1.Show(value, state.context));
        return Disposable.Create(this, static presenter => presenter.Stop());
    }

    private void Stop()
    {
        this.subscription?.Dispose();
        this.subscription = null;
        this.Show(null, null);
        this.root = null;
    }

    private void Show(object? viewModel, DataContext? context)
    {
        if (ReferenceEquals(viewModel, this.ContentViewModel))
        {
            return;
        }

        this.RemoveContent();
        if (viewModel is null || context is null || this.root is null)
        {
            return;
        }

        if (!this.TryGetScene(viewModel.GetType(), out PackedScene? scene))
        {
            GodotBinding.Engine.Errors.Report(new BindingError(
                $"{this.GetPath()}",
                BindingRootBase.ContextKey,
                $"No scene for {viewModel.GetType().FullName}; add it to Views or ViewRegistry, or set Template."));
            return;
        }

        Node content = scene.Instantiate();
        if (content is BindingRootBase contentRoot)
        {
            contentRoot.Context = viewModel;
        }
        else
        {
            this.root.RegisterItemContext(content, new DataContext(viewModel.GetType(), Observable.Return<object?>(viewModel), context));
        }

        this.Content = content;
        this.ContentViewModel = viewModel;
        this.AddChild(content);
    }

    private void RemoveContent()
    {
        if (this.Content is { } content)
        {
            this.root?.UnregisterItemContext(content);
            if (IsInstanceValid(content))
            {
                this.RemoveChild(content);
                content.QueueFree();
            }
        }

        if (this.OwnsContent && this.ContentViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }

        this.Content = null;
        this.ContentViewModel = null;
    }

    private bool TryGetScene(Type viewModelType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PackedScene? scene)
    {
        for (Type? type = viewModelType; type is not null; type = type.BaseType)
        {
            if (type.FullName is { } name && this.Views.TryGetValue(name, out scene))
            {
                return true;
            }
        }

        if (ViewRegistry.TryGetScene(viewModelType, out scene))
        {
            return true;
        }

        scene = this.Template;
        return scene is not null;
    }
}

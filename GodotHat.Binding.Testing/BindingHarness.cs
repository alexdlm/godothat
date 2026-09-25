using System.Diagnostics;
using System.Runtime.CompilerServices;
using Godot;

namespace GodotHat.Binding.Testing;

/// <summary>
/// Mounts a bound scene for a test, with the view model or services the test supplies, and finds its Controls by what
/// they bind to. Works with any test framework that runs in Godot, such as GdUnit4; failures throw
/// <see cref="BindingAssertionException"/>.
/// </summary>
/// <example>
/// <code>
/// var vm = new QueueScreenViewModel(new FakeBuildQueue(3));
/// await using var ui = await BindingHarness.Mount("res://ui/queue_screen.tscn", vm);
///
/// ui.Bound&lt;Button&gt;("Clear").AssertEnabled();
/// await ui.Bound&lt;Button&gt;("Clear").Press();
/// ui.Items("Orders").AssertCount(0);
/// ui.Bound&lt;Button&gt;("Clear").AssertDisabled();
/// ui.AssertNoBindingErrors();
/// </code>
/// </example>
public sealed class BindingHarness : BindingScope, IDisposable, IAsyncDisposable
{
    private readonly List<BindingError> errors = new();
    private readonly IDisposable? ownedViewModel;
    private readonly IViewModelActivator? activator;
    private readonly IViewModelActivator? previousActivator;
    private bool disposed;

    private BindingHarness(Node scene, BindingRootBase root, IDisposable? ownedViewModel, IViewModelActivator? activator)
    {
        this.Scene = scene;
        this.BindingRoot = root;
        this.ownedViewModel = ownedViewModel;
        if (activator is not null)
        {
            this.activator = activator;
            this.previousActivator = GodotBinding.Activator;
            GodotBinding.Activator = activator;
        }

        GodotBinding.ErrorReported += this.OnError;
    }

    /// <summary>The mounted scene.</summary>
    public Node Scene { get; }

    /// <inheritdoc />
    public override Node Node => this.Scene;

    /// <summary>The scene's outermost binding root, which the view model is given to.</summary>
    public BindingRootBase BindingRoot { get; }

    /// <summary>The view model the scene is bound to.</summary>
    public object? ViewModel => this.BindingRoot.ViewModel;

    /// <summary>Every binding error reported while the scene has been mounted, from any scene.</summary>
    public IReadOnlyList<BindingError> Errors
    {
        get
        {
            lock (this.errors)
            {
                return this.errors.ToArray();
            }
        }
    }

    /// <summary>Instantiates the scene at <paramref name="scenePath"/> and adds it to the tree.</summary>
    /// <param name="scenePath">The scene's resource path.</param>
    /// <param name="viewModel">
    /// The view model to bind to, bypassing the scene's own; the test keeps ownership. Omit it to let the scene create its
    /// view model as it would in the game.
    /// </param>
    /// <param name="sample">
    /// The resource path of an <see cref="IViewModelSource"/>, such as the scene's sample data, to create the view model
    /// from, so tests start from the state designers preview. The harness disposes the view model.
    /// </param>
    /// <param name="activator">
    /// Creates the view models the scene and its pages ask for, in place of <see cref="GodotBinding.Activator"/>
    /// until the harness is disposed.
    /// </param>
    /// <param name="services">
    /// Services for view models' constructors, eg a Jab provider with fakes; shorthand for a
    /// <see cref="ServiceProviderViewModelActivator"/> as the <paramref name="activator"/>.
    /// </param>
    public static Task<BindingHarness> Mount(
        string scenePath,
        object? viewModel = null,
        string? sample = null,
        IViewModelActivator? activator = null,
        IServiceProvider? services = null)
    {
        CheckArguments(viewModel, sample, activator, services);
        var scene = GD.Load<PackedScene>(scenePath) ?? throw new ArgumentException($"No scene at {scenePath}.", nameof(scenePath));
        return Mount(scene, viewModel, sample, activator, services);
    }

    /// <summary>Instantiates <paramref name="scene"/> and adds it to the tree.</summary>
    /// <inheritdoc cref="Mount(string, object?, string?, IViewModelActivator?, IServiceProvider?)"/>
    public static Task<BindingHarness> Mount(
        PackedScene scene,
        object? viewModel = null,
        string? sample = null,
        IViewModelActivator? activator = null,
        IServiceProvider? services = null)
    {
        CheckArguments(viewModel, sample, activator, services);
        return Mount(scene.Instantiate(), viewModel, sample, activator, services);
    }

    /// <summary>
    /// Adds <paramref name="scene"/>, eg one built in code, to the tree. The harness frees it when disposed, or now if
    /// mounting fails.
    /// </summary>
    /// <inheritdoc cref="Mount(string, object?, string?, IViewModelActivator?, IServiceProvider?)"/>
    public static async Task<BindingHarness> Mount(
        Node scene,
        object? viewModel = null,
        string? sample = null,
        IViewModelActivator? activator = null,
        IServiceProvider? services = null)
    {
        BindingRootBase root;
        IDisposable? owned = null;
        try
        {
            CheckArguments(viewModel, sample, activator, services);
            root = OutermostRoot(scene) ?? throw new ArgumentException($"{scene.Name} has no binding root.", nameof(scene));
            if (sample is not null)
            {
                if (GD.Load<Resource>(sample) is not IViewModelSource source)
                {
                    throw new ArgumentException($"{sample} is not an IViewModelSource resource.", nameof(sample));
                }

                viewModel = source.CreateViewModel();
                owned = viewModel as IDisposable;
            }
        }
        catch
        {
            scene.Free();
            throw;
        }

        if (viewModel is not null)
        {
            root.Context = viewModel;
        }

        var harness = new BindingHarness(
            scene,
            root,
            owned,
            activator ?? (services is null ? null : new ServiceProviderViewModelActivator(services)));
        Frames.Tree.Root.AddChild(scene);
        await Frames.Next();
        return harness;
    }

    /// <summary>
    /// Waits <paramref name="count"/> frames, for updates delivered on the main thread from elsewhere, such as work
    /// finishing on another thread or bindings that coalesce to once a frame.
    /// </summary>
    public Task Frame(int count = 1) => Frames.Next(count);

    /// <summary>Waits a frame at a time until <paramref name="condition"/> holds, eg for work on another thread.</summary>
    /// <exception cref="BindingAssertionException">It doesn't hold within <paramref name="timeout"/>, 5s by default.</exception>
    public async Task WaitUntil(
        Func<bool> condition,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(condition))] string conditionText = "")
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(5);
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > limit)
            {
                throw new BindingAssertionException($"Timed out after {limit.TotalSeconds:0.#}s waiting for {conditionText}.");
            }

            await Frames.Next();
        }
    }

    /// <summary>Fails if any binding errors have been reported since the scene was mounted.</summary>
    public void AssertNoBindingErrors()
    {
        IReadOnlyList<BindingError> reported = this.Errors;
        if (reported.Count > 0)
        {
            throw new BindingAssertionException(
                $"{reported.Count} binding error(s):{string.Concat(reported.Select(e => $"{System.Environment.NewLine}  {e}"))}");
        }
    }

    /// <summary>
    /// Frees the scene, disposes a view model created from a sample, and restores the activator. Prefer
    /// <see cref="DisposeAsync"/>, which also lets nodes queued for deletion go, so test frameworks don't report them as
    /// orphans.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        if (GodotObject.IsInstanceValid(this.Scene))
        {
            this.Scene.GetParent()?.RemoveChild(this.Scene);
            this.Scene.Free();
        }

        this.ownedViewModel?.Dispose();
        if (this.activator is not null && ReferenceEquals(GodotBinding.Activator, this.activator))
        {
            GodotBinding.Activator = this.previousActivator!;
        }

        GodotBinding.ErrorReported -= this.OnError;
    }

    /// <summary>Disposes the harness, then waits a frame for nodes queued for deletion.</summary>
    public async ValueTask DisposeAsync()
    {
        this.Dispose();
        await Frames.Next();
    }

    private static void CheckArguments(object? viewModel, string? sample, IViewModelActivator? activator, IServiceProvider? services)
    {
        if (viewModel is not null && sample is not null)
        {
            throw new ArgumentException("Pass a view model or a sample, not both.", nameof(sample));
        }

        if (activator is not null && services is not null)
        {
            throw new ArgumentException("Pass an activator or services, not both.", nameof(services));
        }
    }

    private static BindingRootBase? OutermostRoot(Node node)
    {
        if (node is BindingRootBase root)
        {
            return root;
        }

        foreach (Node child in node.GetChildren())
        {
            if (OutermostRoot(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnError(BindingError error)
    {
        lock (this.errors)
        {
            this.errors.Add(error);
        }
    }
}

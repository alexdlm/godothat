using Godot;
using R3;

namespace GodotHat.Binding;

/// <summary>
/// Binds the Controls below it to a view model, using the bindings stored in each Control's <c>godothat_bindings</c>
/// metadata. Scenes use the <c>BindingRoot</c> node from the <c>godothat_binding</c> addon, which derives from this.
/// </summary>
/// <remarks>
/// <para>
/// The view model comes from, in order: <see cref="Context"/>, set by a presenter or test; <see cref="ContextNode"/>;
/// the data context of an enclosing binding root, if it is of the declared <see cref="ViewModelType"/> (or none is
/// declared), as for item templates and components of a page; <see cref="ViewModelType"/>, created through
/// <see cref="GodotBinding.Activator"/> when the scene runs on its own; or <see cref="SampleData"/>. The root disposes
/// only view models it created.
/// </para>
/// <para>
/// Bindings are made each time the root's subtree has entered the tree, after the Controls are ready so they apply
/// over their own initialisation, and removed when it leaves the tree. Nodes added or removed below it later are bound and unbound as they enter and leave. A node's
/// <c>@context</c> binding sets the context for its children; its other bindings use the context it inherits. Nested
/// binding roots bind their own subtrees.
/// </para>
/// </remarks>
public partial class BindingRootBase : Control
{
    /// <summary>The metadata key holding each Control's bindings.</summary>
    public const string BindingsMetaKey = "godothat_bindings";

    /// <summary>The reserved binding key that sets the data context for a Control's subtree.</summary>
    public const string ContextKey = "@context";

    /// <summary>The reserved binding key that binds a container's children, or a list control's items, to a collection.</summary>
    public const string ItemsKey = "@items";

    /// <summary>The reserved binding key for the selected item of an ItemList, OptionButton or Tree bound with <c>@items</c>.</summary>
    public const string SelectedKey = "@selected";

    /// <summary>Metadata marking a node whose descendants are a design-time preview, in which binding roots bind in the editor.</summary>
    public const string DesignTimePreviewMetaKey = "_godothat_preview";

    private static readonly StringName BindingsMeta = BindingsMetaKey;
    private static readonly StringName DesignTimePreviewMeta = DesignTimePreviewMetaKey;

    private readonly OriginalValues originals = new();
    private readonly Dictionary<ulong, NodeBindings> nodes = new();
    private readonly List<BindingRootBase> nestedRoots = new();
    private readonly Dictionary<ulong, DataContext> itemContexts = new();
    private readonly CompositeDisposable rootSubscriptions = new();

    private object? context;
    private bool hasContext;
    private DataContext? inheritedContext;
    private IDisposable? ownedViewModel;
    private bool active;

    /// <summary>Creates the root.</summary>
    public BindingRootBase()
    {
        this.TreeExiting += this.OnTreeExitingInternal;
    }

    /// <summary>
    /// Full name of the <c>[ViewModel]</c> type the scene binds to: checked by editor tooling, and created through
    /// <see cref="GodotBinding.Activator"/> when nothing else supplies a view model.
    /// </summary>
    [Export]
    public string ViewModelType { get; set; } = "";

    /// <summary>A node to bind to directly, such as a <c>[ViewModel]</c> manager node.</summary>
    [Export]
    public Node? ContextNode { get; set; }

    /// <summary>
    /// A resource implementing <see cref="IViewModelSource"/>, used when nothing else supplies a view model, eg when
    /// running a component scene on its own.
    /// </summary>
    [Export]
    public Resource? SampleData { get; set; }

    /// <summary>
    /// The view model to bind to, supplied by a presenter or test before or after the root enters the tree. Setting
    /// it rebinds; the root doesn't take ownership.
    /// </summary>
    public object? Context
    {
        get => this.context;
        set
        {
            this.context = value;
            this.hasContext = true;
            this.Rebind();
        }
    }

    /// <summary>The data context bindings below this root resolve against, or null when unbound.</summary>
    public DataContext? DataContext { get; private set; }

    /// <summary>The view model currently bound, from whichever source supplied it; null when unbound or inherited.</summary>
    public object? ViewModel { get; private set; }

    /// <summary>Whether bindings are currently applied.</summary>
    public bool IsBound => this.DataContext is not null;

    /// <summary>
    /// Finds the view model when no <see cref="Context"/> is given. Override to supply one another way.
    /// </summary>
    /// <param name="owned">Whether the root created the view model and must dispose it.</param>
    protected virtual object? ResolveViewModel(out bool owned)
    {
        owned = false;
        if (this.ContextNode is { } contextNode)
        {
            return contextNode;
        }

        // Inside another root, eg as an item template or a component of a page, use the context passed down when it is
        // of the declared type; null means use the inherited context.
        Type? declared = this.DeclaredViewModelType;
        if (this.inheritedContext is { } inherited && (declared is null || declared.IsAssignableFrom(inherited.Type)))
        {
            return null;
        }

        // Previews in the editor use sample data, as the game's services aren't set up there
        if (this.IsDesignTimePreview && this.SampleData is IViewModelSource preview)
        {
            owned = true;
            return preview.CreateViewModel();
        }

        if (declared is not null)
        {
            owned = true;
            return GodotBinding.Activator.Create(declared);
        }

        if (!string.IsNullOrEmpty(this.ViewModelType))
        {
            this.ReportError($"View model type '{this.ViewModelType}' is not a registered [ViewModel].");
            return null;
        }

        if (this.SampleData is IViewModelSource source)
        {
            owned = true;
            return source.CreateViewModel();
        }

        return null;
    }

    /// <summary>
    /// The view model type the root declares with <see cref="ViewModelType"/>, or null. Editor tooling and
    /// <c>BindingValidator</c> check the bindings below the root against it.
    /// </summary>
    public Type? DeclaredViewModelType =>
        !string.IsNullOrEmpty(this.ViewModelType) && BindingRegistry.TryGetByName(this.ViewModelType, out ViewModelAccessor? accessor)
            ? accessor.ViewModelType
            : null;

    /// <summary>
    /// Whether the root is part of a design-time preview in the editor, marked by the <see cref="DesignTimePreviewMetaKey"/>
    /// metadata on an ancestor. Roots only bind in the editor within a preview.
    /// </summary>
    public bool IsDesignTimePreview
    {
        get
        {
            for (Node? node = this; node is not null; node = node.GetParent())
            {
                if (node.HasMeta(DesignTimePreviewMeta))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Called after bindings are applied.</summary>
    protected virtual void OnBound()
    {
    }

    /// <summary>Called after bindings are removed.</summary>
    protected virtual void OnUnbound()
    {
    }

    /// <summary>Lists registered view models as suggestions for <see cref="ViewModelType"/> in the inspector.</summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        // Godot calls this for every property each time it lists them, with a new wrapper; free it now, not by finalizer
        using (property)
        {
            if (GodotData.String(property, GodotData.NameKey) != nameof(this.ViewModelType))
            {
                return;
            }

            BindingRegistry.Discover();
            property[GodotData.HintKey] = (int)PropertyHint.EnumSuggestion;
            using Variant hintString = string.Join(
                ",",
                BindingRegistry.Accessors.Select(a => a.ViewModelType.FullName).Order(StringComparer.Ordinal));
            property[GodotData.HintStringKey] = hintString;
        }
    }

    // Item instances are registered before they're added, so they bind with their item as data context.
    internal void RegisterItemContext(Node instance, DataContext context) => this.itemContexts[instance.GetInstanceId()] = context;

    internal void UnregisterItemContext(Node instance) => this.itemContexts.Remove(instance.GetInstanceId());

    // The instances an @items binding on a container has added, in item order, for GodotHat.Binding.Testing.
    internal IReadOnlyList<Node>? ItemInstances(Node container) =>
        this.nodes.TryGetValue(container.GetInstanceId(), out NodeBindings? state) ? state.Items?.Instances : null;

    // A nested root with no view model of its own binds to the enclosing root's context.
    internal void Inherit(DataContext? parentContext)
    {
        if (ReferenceEquals(this.inheritedContext, parentContext))
        {
            return;
        }

        this.inheritedContext = parentContext;
        this.Rebind();
    }

    /// <summary>
    /// Binds after the root and its subtree enter the tree. Subclasses overriding this must call the base
    /// implementation.
    /// </summary>
    public override void _Notification(int what)
    {
        // Sent each time the subtree has entered the tree, after the children are ready the first time, so bindings
        // apply over the Controls' own initialisation.
        if (what == NotificationPostEnterTree)
        {
            this.Activate();
        }
        else if (what == NotificationPredelete)
        {
            this.originals.Dispose();
        }
    }

    private void OnTreeExitingInternal()
    {
        this.active = false;
        this.Unbind();
    }

    private void Activate()
    {
        if (this.active || (Engine.IsEditorHint() && !this.IsDesignTimePreview))
        {
            return;
        }

        this.active = true;
        this.Bind();
    }

    private void Rebind()
    {
        if (!this.active)
        {
            return;
        }

        this.Unbind();
        this.Bind();
    }

    private void Bind()
    {
        DataContext? root = this.CreateRootContext();
        if (root is null)
        {
            return;
        }

        this.DataContext = root;
        this.Visit(this, root);
        this.OnBound();
    }

    private void Unbind()
    {
        bool wasBound = this.IsBound;
        foreach (NodeBindings state in this.nodes.Values.ToArray())
        {
            state.Dispose();
        }

        this.nodes.Clear();
        foreach (BindingRootBase nested in this.nestedRoots)
        {
            if (IsInstanceValid(nested))
            {
                nested.Inherit(null);
            }
        }

        this.nestedRoots.Clear();
        this.itemContexts.Clear();
        this.rootSubscriptions.Clear();
        this.DataContext = null;
        this.ViewModel = null;

        this.ownedViewModel?.Dispose();
        this.ownedViewModel = null;

        if (wasBound)
        {
            this.OnUnbound();
        }
    }

    private DataContext? CreateRootContext()
    {
        object? viewModel;
        if (this.hasContext)
        {
            viewModel = this.context;
        }
        else
        {
            try
            {
                viewModel = this.ResolveViewModel(out bool owned);
                if (owned)
                {
                    this.ownedViewModel = viewModel as IDisposable;
                }
            }
            catch (Exception e)
            {
                this.ReportError("Failed to create the view model.", e);
                return null;
            }
        }

        if (viewModel is null)
        {
            return this.inheritedContext;
        }

        this.ViewModel = viewModel;

        var values = new ReactiveProperty<object?>(viewModel, ReferenceEqualityComparer.Instance).AddTo(this.rootSubscriptions);
        var rootContext = new DataContext(viewModel.GetType(), values, this.inheritedContext);
        return NodeContextGuard.Guard(rootContext, this.rootSubscriptions);
    }

    // Binds a node, then its children unless it has only just entered the tree: its children enter after it and are
    // visited through its child-entered hook, so nodes are always in the tree when bound.
    private void Visit(Node node, DataContext parentContext, bool children = true)
    {
        if (this.itemContexts.TryGetValue(node.GetInstanceId(), out DataContext? itemContext))
        {
            parentContext = itemContext;
        }

        if (node != this && node is BindingRootBase nested)
        {
            this.nestedRoots.Add(nested);
            nested.Inherit(parentContext);
            return;
        }

        ulong id = node.GetInstanceId();
        if (this.nodes.ContainsKey(id))
        {
            return;
        }

        var state = new NodeBindings(this, node);
        this.nodes[id] = state;
        DataContext context = this.BindNode(node, parentContext, state);
        state.Context = context;
        state.Hook();

        if (node != this && node is ContentPresenterBase presenter)
        {
            // Without its own @context a presenter would present the view model it's part of, forever
            if (ReferenceEquals(context, parentContext))
            {
                this.ReportError($"{node.GetPath()}: a ContentPresenter needs an '{ContextKey}' binding to the view model it shows.");
            }
            else
            {
                state.Add(presenter.Present(this, context));
            }
        }

        if (!children)
        {
            return;
        }

        foreach (Node child in node.GetChildren())
        {
            this.Visit(child, context);
        }
    }

    private void Forget(Node node)
    {
        if (node is BindingRootBase nested && nested != this)
        {
            this.nestedRoots.Remove(nested);
            nested.Inherit(null);
            return;
        }

        // Children leave the tree before their parent, through its child-exiting hook, so only this node remains.
        if (this.nodes.Remove(node.GetInstanceId(), out NodeBindings? state))
        {
            state.Dispose();
        }
    }

    private DataContext BindNode(Node node, DataContext context, NodeBindings state)
    {
        if (!node.HasMeta(BindingsMeta))
        {
            return context;
        }

        if (GodotData.ReadMetaDictionary(node, BindingsMeta) is not { } entries)
        {
            this.ReportError($"{node.GetPath()}: '{BindingsMetaKey}' metadata must be a dictionary of BindingDef.");
            return context;
        }

        BindingEngine engine = GodotBinding.Engine;
        BindingDefBase? contextDefinition = null;
        BindingDefBase? itemsDefinition = null;
        BindingDefBase? selectedDefinition = null;
        var bindings = new List<(string Key, BindingDefBase Definition)>();
        foreach ((string name, GodotObject? value) in entries)
        {
            if (value is not BindingDefBase definition)
            {
                this.ReportError($"{node.GetPath()}: binding '{name}' is not a BindingDef.");
                continue;
            }

            switch (name)
            {
                case ContextKey:
                    contextDefinition = definition;
                    break;
                case ItemsKey:
                    itemsDefinition = definition;
                    break;
                case SelectedKey:
                    selectedDefinition = definition;
                    break;
                default:
                    bindings.Add((name, definition));
                    break;
            }
        }

        ListItemsTarget? listItems = null;
        if (itemsDefinition is not null)
        {
            listItems = ListItemsTarget.For(node, itemsDefinition, context);
            IItemsTarget items = listItems ?? (IItemsTarget)(state.Items = new ContainerItemsTarget(this, node, itemsDefinition, context));
            state.Add(engine.BindItems(context, itemsDefinition.ToSpec(), items));
            state.Add((IDisposable)items);
        }

        if (selectedDefinition is not null)
        {
            if (listItems is null)
            {
                this.ReportError($"{node.GetPath()}: '{SelectedKey}' needs an '{ItemsKey}' binding on an ItemList, OptionButton or Tree.");
            }
            else
            {
                state.Add(engine.BindProperty(context, selectedDefinition.ToSpec(), new ListSelectionTarget(listItems, node)));
            }
        }

        foreach ((string key, BindingDefBase definition) in bindings)
        {
            BindingSpec spec = definition.ToSpec();
            switch (spec.Kind)
            {
                case BindingKind.Property:
                    state.Add(engine.BindProperty(context, spec, new NodePropertyTarget(node, key, definition, this.originals)));
                    break;
                case BindingKind.Command:
                    state.Add(engine.BindCommand(context, spec, new SignalCommandTarget(node, key, engine.Errors, spec.Path)));
                    break;
                case BindingKind.Event:
                    state.Add(engine.BindEvent(context, spec, new MethodEventTarget(node, key)));
                    break;
                default:
                    this.ReportError($"{node.GetPath()}: binding '{key}' has kind {spec.Kind}, which only applies to '{ContextKey}' or '{ItemsKey}'.");
                    break;
            }
        }

        // The node's own bindings above use the context it inherits, so eg a panel can bind its visibility to whether
        // the view model it shows exists; the context it sets applies to its children.
        if (contextDefinition is null)
        {
            return context;
        }

        DataContext bound = engine.BindContext(
            context,
            contextDefinition.ToSpec(),
            $"{node.GetPath()}:{ContextKey}",
            out IDisposable subscription);
        state.Add(subscription);
        return NodeContextGuard.Guard(bound, state.Subscriptions);
    }

    private void ReportError(string message, Exception? exception = null) =>
        GodotBinding.Engine.Errors.Report(new BindingError(this.IsInsideTree() ? this.GetPath() : this.Name, "", message, exception));

    // The bindings on one node, and its hooks for children entering and leaving.
    private sealed class NodeBindings(BindingRootBase root, Node node) : IDisposable
    {
        private bool hooked;

        public CompositeDisposable Subscriptions { get; } = new();

        public DataContext? Context { get; set; }

        public ContainerItemsTarget? Items { get; set; }

        public void Add(IDisposable subscription) => this.Subscriptions.Add(subscription);

        public void Hook()
        {
            node.ChildEnteredTree += this.OnChildEntered;
            node.ChildExitingTree += this.OnChildExiting;
            this.hooked = true;
        }

        public void Dispose()
        {
            if (this.hooked && IsInstanceValid(node))
            {
                node.ChildEnteredTree -= this.OnChildEntered;
                node.ChildExitingTree -= this.OnChildExiting;
            }

            this.hooked = false;
            this.Subscriptions.Dispose();
        }

        private void OnChildEntered(Node child)
        {
            if (this.Context is { } context)
            {
                root.Visit(child, context, children: false);
            }
        }

        private void OnChildExiting(Node child) => root.Forget(child);
    }
}

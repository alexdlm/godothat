using Godot;

namespace GodotHat.Binding;

/// <summary>
/// One binding on a Control, stored in its <c>godothat_bindings</c> metadata under the target's name: a property
/// (eg <c>text</c>), a signal for command bindings (eg <c>pressed</c>), a method for event bindings, or the reserved
/// keys <c>@context</c> and <c>@items</c>.
/// </summary>
/// <remarks>
/// Scenes use the <c>BindingDef</c> resource from the <c>godothat_binding</c> addon, which derives from this; Godot
/// only loads scripts from the game's own assembly. Treat instances as read-only at runtime, as scenes share them.
/// </remarks>
public partial class BindingDefBase : Resource
{
    private BindingKind kind;
    private string path = "";
    private BindingMode mode;
    private StringName converter = new();
    private string converterArg = "";
    private BindingSourceKind source;
    private BindingParameterKind parameter;
    private string parameterValue = "";
    private bool coalesce;
    private BindingSpec? spec;

    /// <summary>What the binding connects to.</summary>
    [Export]
    public BindingKind Kind
    {
        get => this.kind;
        set => this.Change(ref this.kind, value);
    }

    /// <summary>Dotted member path from the data context, eg <c>Selected.Name</c>.</summary>
    [Export]
    public string Path
    {
        get => this.path;
        set => this.Change(ref this.path, value);
    }

    /// <summary>Direction and frequency, for property bindings.</summary>
    [Export]
    public BindingMode Mode
    {
        get => this.mode;
        set => this.Change(ref this.mode, value);
    }

    /// <summary>Registered converter id, eg <c>not</c> or <c>format</c>; empty for none.</summary>
    [Export]
    public StringName Converter
    {
        get => this.converter;
        set => this.Change(ref this.converter, value);
    }

    /// <summary>Converter argument, eg a format string.</summary>
    [Export]
    public string ConverterArg
    {
        get => this.converterArg;
        set => this.Change(ref this.converterArg, value);
    }

    /// <summary>Which data context the path starts from.</summary>
    [Export]
    public BindingSourceKind Source
    {
        get => this.source;
        set => this.Change(ref this.source, value);
    }

    /// <summary>The parameter passed when a command binding executes.</summary>
    [Export]
    public BindingParameterKind Parameter
    {
        get => this.parameter;
        set => this.Change(ref this.parameter, value);
    }

    /// <summary>The command parameter, when <see cref="Parameter"/> is <see cref="BindingParameterKind.Constant"/>.</summary>
    [Export]
    public string ParameterValue
    {
        get => this.parameterValue;
        set => this.Change(ref this.parameterValue, value);
    }

    /// <summary>
    /// The scene instantiated per item, for <c>@items</c> bindings on containers; each instance's data context is its
    /// item.
    /// </summary>
    [Export]
    public PackedScene? Template { get; set; }

    /// <summary>
    /// For <c>@items</c> bindings on an ItemList, OptionButton or Tree: the path from each item to its text; empty
    /// shows the item itself. <see cref="Converter"/> applies to it.
    /// </summary>
    [Export]
    public string ItemText { get; set; } = "";

    /// <summary>For <c>@items</c> bindings on an ItemList, OptionButton or Tree: the path from each item to its icon.</summary>
    [Export]
    public string ItemIcon { get; set; } = "";

    /// <summary>
    /// Applied when the path can't be resolved, eg a null member along it. When unset, the property's value from the
    /// scene is restored.
    /// </summary>
    [Export]
    public Variant Fallback { get; set; }

    /// <summary>Apply only the last value each frame, for sources that change faster than they're shown.</summary>
    [Export]
    public bool Coalesce
    {
        get => this.coalesce;
        set => this.Change(ref this.coalesce, value);
    }

    /// <summary>The binding as a Godot-free <see cref="BindingSpec"/>, for the binding engine.</summary>
    public BindingSpec ToSpec() =>
        this.spec ??= new BindingSpec
        {
            Kind = this.kind,
            Path = this.path,
            Mode = this.mode,
            Converter = this.converter.IsEmpty ? null : this.converter.ToString(),
            ConverterArgument = string.IsNullOrEmpty(this.converterArg) ? null : this.converterArg,
            Source = this.source,
            Parameter = this.parameter,
            ParameterValue = this.parameterValue,
            Coalesce = this.coalesce,
        };

    private void Change<T>(ref T field, T value)
    {
        field = value;
        this.spec = null;
    }
}

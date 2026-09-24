namespace GodotHat.Binding;

/// <summary>What a binding connects to on its target.</summary>
public enum BindingKind
{
    /// <summary>A target property, updated from the source (and back for two-way bindings).</summary>
    Property = 0,

    /// <summary>A target signal that executes a view model command.</summary>
    Command = 1,

    /// <summary>A view model observable that calls a method on the target for each value.</summary>
    Event = 2,

    /// <summary>Sets the data context for the target's subtree.</summary>
    Context = 3,

    /// <summary>Binds a container's children to a collection.</summary>
    Items = 4,
}

/// <summary>Direction and frequency of a property binding.</summary>
public enum BindingMode
{
    /// <summary>Source to target, on every change.</summary>
    OneWay = 0,

    /// <summary>Source to target, and target changes are written back to the source.</summary>
    TwoWay = 1,

    /// <summary>Source to target once, when the first value is available.</summary>
    OneTime = 2,
}

/// <summary>Which data context a binding's path starts from.</summary>
public enum BindingSourceKind
{
    /// <summary>The nearest data context.</summary>
    Context = 0,

    /// <summary>
    /// The data context above the nearest one, for example to reach the page view model from inside an item template.
    /// </summary>
    ParentContext = 1,
}

/// <summary>The parameter passed when a command binding executes.</summary>
public enum BindingParameterKind
{
    /// <summary>The command's default parameter value.</summary>
    None = 0,

    /// <summary>The target's own data context, typically the current item in an item template.</summary>
    Item = 1,

    /// <summary><see cref="BindingSpec.ParameterValue"/> parsed to the command's parameter type.</summary>
    Constant = 2,
}

/// <summary>
/// A binding's definition, independent of Godot. The Godot adapter reads these from each Control's
/// <c>godothat_bindings</c> metadata.
/// </summary>
public sealed class BindingSpec
{
    /// <summary>What the binding connects to.</summary>
    public BindingKind Kind { get; init; }

    /// <summary>Dotted member path from the data context, eg <c>Selected.Name</c>. Empty binds the context itself.</summary>
    public string Path { get; init; } = "";

    /// <summary>Direction and frequency, for property bindings.</summary>
    public BindingMode Mode { get; init; }

    /// <summary>Registered converter id, eg <c>not</c> or <c>format</c>, or null for none.</summary>
    public string? Converter { get; init; }

    /// <summary>Converter argument, eg a format string.</summary>
    public string? ConverterArgument { get; init; }

    /// <summary>Which data context the path starts from.</summary>
    public BindingSourceKind Source { get; init; }

    /// <summary>The parameter for command bindings.</summary>
    public BindingParameterKind Parameter { get; init; }

    /// <summary>The constant parameter, when <see cref="Parameter"/> is <see cref="BindingParameterKind.Constant"/>.</summary>
    public string? ParameterValue { get; init; }

    /// <summary>
    /// Apply only the last value each frame, for sources that change more often than they're displayed.
    /// </summary>
    public bool Coalesce { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{this.Kind} '{this.Path}'";
}

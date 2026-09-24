using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotHat.Binding;

// Property values a binding root saw before binding, restored as the fallback when a binding has none.
internal sealed class OriginalValues : IDisposable
{
    private readonly Dictionary<(ulong Node, string Property), Variant> values = new();

    public Variant GetOrCapture(GodotObject target, string property)
    {
        if (!this.values.TryGetValue((target.GetInstanceId(), property), out Variant value))
        {
            value = target.Get(property);
            this.values[(target.GetInstanceId(), property)] = value;
        }

        return value;
    }

    public void Dispose()
    {
        foreach (Variant value in this.values.Values)
        {
            value.Dispose();
        }

        this.values.Clear();
    }
}

// A property of a Godot object as a binding target.
internal sealed class NodePropertyTarget(
    Node node,
    string property,
    BindingDefBase definition,
    OriginalValues originals) : IBindingTarget
{
    private readonly StringName propertyName = property;
    private string? description;
    private PropertyMeta meta;
    private Mode mode;
    private Delegate? setter;
    private Delegate? getter;
    private object? enumInfo;
    private IReadOnlyList<HintOption>? hintOptions;
    private RangeHint? range;
    private bool rangeWarned;

    private enum Mode
    {
        Direct,
        HintEnumByName,
        OptionButtonEnum,
    }

    public string Description => this.description ??= $"{node.GetPath()}:{property}";

    public bool CanAccept<T>([NotNullWhen(false)] out string? error)
    {
        if (!PropertyInfoCache.TryGetProperty(node, property, out this.meta))
        {
            // C# script members that aren't exported, eg [BindableTarget] ones, aren't listed in editor builds, but
            // Godot's generated code still sets them by name
            if (node.GetScript().Obj is not CSharpScript)
            {
                error = $"{node.GetClass()} has no property '{property}'.";
                return false;
            }

            this.meta = new PropertyMeta(property, Variant.Type.Nil, PropertyHint.None, "", PropertyUsageFlags.None);
        }

        originals.GetOrCapture(node, property);

        if (node is OptionButton && property == "selected" && BindingRegistry.TryGetEnum(out IEnumInfo<T>? info))
        {
            this.mode = Mode.OptionButtonEnum;
            this.enumInfo = info;
            this.PopulateOptions(typeof(T));
            error = null;
            return true;
        }

        if (typeof(T) == typeof(string) && this.meta.Type == Variant.Type.Int && this.meta.Hint == PropertyHint.Enum)
        {
            this.mode = Mode.HintEnumByName;
            this.hintOptions = HintOption.ParseEnum(this.meta.HintString);
            error = null;
            return true;
        }

        Variant.Type? sourceType = VariantTypes.Of<T>();
        if (sourceType is not { } type)
        {
            error = $"{typeof(T).Name} values can't be passed to Godot; add a converter such as to_string or format.";
            return false;
        }

        if (!VariantTypes.CanAssign(type, this.meta.Type))
        {
            error = $"'{property}' is {this.meta.Type}, but the binding gives {typeof(T).Name}; add a converter such as to_string or format.";
            return false;
        }

        this.setter = FastAccess.Setter<T>(node, property);
        this.getter = FastAccess.Getter<T>(node, property);
        if (this.meta.Hint == PropertyHint.Range && OS.IsDebugBuild() && RangeHint.TryParse(this.meta.HintString, out RangeHint hint))
        {
            this.range = hint;
        }

        error = null;
        return true;
    }

    public void SetValue<[MustBeVariant] T>(T value)
    {
        switch (this.mode)
        {
            case Mode.OptionButtonEnum:
                this.SelectOption(((IEnumInfo<T>)this.enumInfo!).GetName(value));
                return;
            case Mode.HintEnumByName:
                this.SetHintEnum((string?)(object?)value);
                return;
        }

        // Out of range values are applied as they are, never clamped; each binding warns once, in debug builds
        if (this.range is { } hint && !this.rangeWarned && VariantTypes.TryToDouble(value, out double number) && !hint.Contains(number))
        {
            this.rangeWarned = true;
            GD.PushWarning($"{this.Description}: {number} is outside its range {hint.Min} to {hint.Max}.");
        }

        if (this.setter is Action<GodotObject, T> typed)
        {
            typed(node, value);
            return;
        }

        using Variant variant = Variant.From(value);
        node.Set(this.propertyName, variant);
    }

    public void SetFallback()
    {
        Variant fallback = definition.Fallback;
        node.Set(
            this.propertyName,
            fallback.VariantType == Variant.Type.Nil ? originals.GetOrCapture(node, property) : fallback);
    }

    public bool TryReadValue<[MustBeVariant] T>(out T value)
    {
        switch (this.mode)
        {
            case Mode.OptionButtonEnum:
                var option = (OptionButton)node;
                return ((IEnumInfo<T>)this.enumInfo!).TryParse(
                    option.Selected >= 0 ? option.GetItemText(option.Selected) : null,
                    out value);
            case Mode.HintEnumByName:
                long current = node.Get(this.propertyName).AsInt64();
                string? name = this.hintOptions!.FirstOrDefault(o => o.Value == current).Name;
                value = (T)(object)(name ?? "");
                return name is not null;
        }

        if (this.getter is Func<GodotObject, T> typed)
        {
            value = typed(node);
            return true;
        }

        using Variant variant = node.Get(this.propertyName);
        value = variant.As<T>();
        return true;
    }

    public IDisposable? ObserveChanges(Action onChanged)
    {
        if (!TwoWayMap.TryGetSignal(node, property, out string signal) ||
            !SignalConnection.TryConnect(node, signal, onChanged, out SignalConnection? connection, out string? error))
        {
            return null;
        }

        return connection;
    }

    // An OptionButton bound to an enum lists the enum's names, unless the scene already defines items; either way
    // items are matched to values by name.
    private void PopulateOptions(Type enumType)
    {
        var option = (OptionButton)node;
        if (option.ItemCount > 0 || !BindingRegistry.TryGetEnum(enumType, out EnumInfo? info))
        {
            return;
        }

        foreach (string name in info.Names)
        {
            option.AddItem(name);
        }
    }

    private void SelectOption(string? name)
    {
        var option = (OptionButton)node;
        for (int i = 0; i < option.ItemCount; i++)
        {
            if (option.GetItemText(i) == name)
            {
                option.Select(i);
                return;
            }
        }

        option.Select(-1);
    }

    private void SetHintEnum(string? name)
    {
        foreach (HintOption option in this.hintOptions!)
        {
            if (option.Name == name)
            {
                node.Set(this.propertyName, option.Value);
                return;
            }
        }

        GodotBinding.Engine.Errors.Report(new BindingError(this.Description, definition.Path, $"'{name}' is not one of {this.meta.HintString}."));
    }
}

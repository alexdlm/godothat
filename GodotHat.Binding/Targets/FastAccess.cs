using Godot;

namespace GodotHat.Binding;

// Typed accessors for common Control properties. They skip Variant, which allocates tracking objects for strings and
// objects, and use the no-signal setters so bound writes don't look like user edits.
internal static class FastAccess
{
    public static Action<GodotObject, T>? Setter<T>(GodotObject target, string property)
    {
        Delegate? setter = typeof(T) == typeof(string) ? StringSetter(target, property)
            : typeof(T) == typeof(bool) ? BoolSetter(target, property)
            : typeof(T) == typeof(double) ? DoubleSetter(target, property)
            : typeof(T) == typeof(float) && target is Godot.Range && property == "value" ? (Action<GodotObject, float>)((t, v) => ((Godot.Range)t).SetValueNoSignal(v))
            : typeof(T) == typeof(int) ? IntSetter(target, property)
            : typeof(T) == typeof(Color) && target is CanvasItem && property is "modulate" ? (Action<GodotObject, Color>)((t, v) => ((CanvasItem)t).Modulate = v)
            : typeof(T) == typeof(Color) && target is CanvasItem && property is "self_modulate" ? (Action<GodotObject, Color>)((t, v) => ((CanvasItem)t).SelfModulate = v)
            : null;
        return setter as Action<GodotObject, T>;
    }

    public static Func<GodotObject, T>? Getter<T>(GodotObject target, string property)
    {
        Delegate? getter = typeof(T) == typeof(string) && property == "text" ? target switch
            {
                LineEdit => (Func<GodotObject, string>)(t => ((LineEdit)t).Text),
                TextEdit => (Func<GodotObject, string>)(t => ((TextEdit)t).Text),
                _ => null,
            }
            : typeof(T) == typeof(double) && target is Godot.Range && property == "value" ? (Func<GodotObject, double>)(t => ((Godot.Range)t).Value)
            : typeof(T) == typeof(bool) && target is BaseButton && property == "button_pressed" ? (Func<GodotObject, bool>)(t => ((BaseButton)t).ButtonPressed)
            : typeof(T) == typeof(int) && target is OptionButton && property == "selected" ? (Func<GodotObject, int>)(t => ((OptionButton)t).Selected)
            : null;
        return getter as Func<GodotObject, T>;
    }

    private static Delegate? StringSetter(GodotObject target, string property) => property switch
    {
        "text" => target switch
        {
            Label => (Action<GodotObject, string>)((t, v) => ((Label)t).Text = v),
            Button => (Action<GodotObject, string>)((t, v) => ((Button)t).Text = v),
            LineEdit => (Action<GodotObject, string>)((t, v) => ((LineEdit)t).Text = v),
            TextEdit => (Action<GodotObject, string>)((t, v) => ((TextEdit)t).Text = v),
            RichTextLabel => (Action<GodotObject, string>)((t, v) => ((RichTextLabel)t).Text = v),
            _ => null,
        },
        "tooltip_text" when target is Control => (Action<GodotObject, string>)((t, v) => ((Control)t).TooltipText = v),
        "placeholder_text" when target is LineEdit => (Action<GodotObject, string>)((t, v) => ((LineEdit)t).PlaceholderText = v),
        _ => null,
    };

    private static Delegate? BoolSetter(GodotObject target, string property) => property switch
    {
        "visible" when target is CanvasItem => (Action<GodotObject, bool>)((t, v) => ((CanvasItem)t).Visible = v),
        "disabled" when target is BaseButton => (Action<GodotObject, bool>)((t, v) => ((BaseButton)t).Disabled = v),
        "button_pressed" when target is BaseButton => (Action<GodotObject, bool>)((t, v) => ((BaseButton)t).SetPressedNoSignal(v)),
        "editable" when target is LineEdit => (Action<GodotObject, bool>)((t, v) => ((LineEdit)t).Editable = v),
        _ => null,
    };

    private static Delegate? DoubleSetter(GodotObject target, string property) =>
        target is Godot.Range && property == "value" ? (Action<GodotObject, double>)((t, v) => ((Godot.Range)t).SetValueNoSignal(v)) : null;

    private static Delegate? IntSetter(GodotObject target, string property) => property switch
    {
        "value" when target is Godot.Range => (Action<GodotObject, int>)((t, v) => ((Godot.Range)t).SetValueNoSignal(v)),
        "selected" when target is OptionButton => (Action<GodotObject, int>)((t, v) => ((OptionButton)t).Select(v)),
        _ => null,
    };
}

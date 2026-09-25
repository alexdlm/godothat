using System.Collections.Concurrent;
using Godot;

namespace GodotHat.Binding;

/// <summary>
/// The signal that reports user changes to a Control property, which two-way bindings listen to. Built-in entries
/// cover LineEdit and TextEdit text, Range value, BaseButton button_pressed, OptionButton selected, TabContainer and
/// TabBar current_tab, and ColorPicker and ColorPickerButton color.
/// </summary>
/// <example>
/// <code>
/// TwoWayMap.Register("SpinBox", "value", "value_changed");
/// TwoWayMap.Register&lt;VolumeKnob&gt;("Level", "LevelChanged");
/// </code>
/// </example>
public static class TwoWayMap
{
    private static readonly ConcurrentDictionary<(string Class, string Property), string> byClass = new()
    {
        [("LineEdit", "text")] = "text_changed",
        [("TextEdit", "text")] = "text_changed",
        [("Range", "value")] = "value_changed",
        [("BaseButton", "button_pressed")] = "toggled",
        [("OptionButton", "selected")] = "item_selected",
        [("TabContainer", "current_tab")] = "tab_changed",
        [("TabBar", "current_tab")] = "tab_changed",
        [("ColorPicker", "color")] = "color_changed",
        [("ColorPickerButton", "color")] = "color_changed",
    };

    private static readonly ConcurrentDictionary<(Type Type, string Property), string> byType = new();

    /// <summary>Registers the change signal for a property of a Godot class and its subclasses.</summary>
    public static void Register(string godotClass, string property, string signal) =>
        byClass[(godotClass, property)] = signal;

    /// <summary>Registers the change signal for a property of a C# node type and its subclasses.</summary>
    public static void Register<TNode>(string property, string signal)
        where TNode : GodotObject =>
        byType[(typeof(TNode), property)] = signal;

    /// <summary>Finds the change signal for <paramref name="property"/> of <paramref name="target"/>.</summary>
    public static bool TryGetSignal(GodotObject target, string property, out string signal)
    {
        foreach (KeyValuePair<(Type Type, string Property), string> entry in byType)
        {
            if (entry.Key.Property == property && entry.Key.Type.IsInstanceOfType(target))
            {
                signal = entry.Value;
                return true;
            }
        }

        for (string godotClass = target.GetClass(); !string.IsNullOrEmpty(godotClass); godotClass = ClassDB.GetParentClass(godotClass))
        {
            if (byClass.TryGetValue((godotClass, property), out string? found))
            {
                signal = found;
                return true;
            }
        }

        signal = "";
        return false;
    }
}

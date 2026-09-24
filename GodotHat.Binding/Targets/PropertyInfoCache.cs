using System.Collections.Concurrent;
using Godot;
using Godot.Collections;

namespace GodotHat.Binding;

/// <summary>A property as Godot describes it, from <see cref="GodotObject.GetPropertyList"/>.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Type">Its Variant type; <see cref="Variant.Type.Nil"/> for properties that take any Variant.</param>
/// <param name="Hint">Its editor hint.</param>
/// <param name="HintString">Its hint string, eg <c>0,100,1</c> for a range.</param>
/// <param name="Usage">Its usage flags.</param>
public readonly record struct PropertyMeta(
    string Name,
    Variant.Type Type,
    PropertyHint Hint,
    string HintString,
    PropertyUsageFlags Usage);

// Property and signal metadata per class and script, read once from Godot.
internal static class PropertyInfoCache
{
    private static readonly ConcurrentDictionary<string, System.Collections.Generic.Dictionary<string, PropertyMeta>> properties = new();
    private static readonly ConcurrentDictionary<string, System.Collections.Generic.Dictionary<string, int>> signals = new();

    public static bool TryGetProperty(GodotObject target, string name, out PropertyMeta meta) =>
        properties.GetOrAdd(KeyOf(target), _ => ReadProperties(target)).TryGetValue(name, out meta);

    public static bool TryGetSignalArgumentCount(GodotObject target, string name, out int count) =>
        signals.GetOrAdd(KeyOf(target), _ => ReadSignals(target)).TryGetValue(name, out count);

    private static string KeyOf(GodotObject target)
    {
        using Variant script = target.GetScript();
        return script.Obj is Script resource ? $"{target.GetClass()}|{resource.ResourcePath}" : target.GetClass();
    }

    private static System.Collections.Generic.Dictionary<string, PropertyMeta> ReadProperties(GodotObject target)
    {
        var result = new System.Collections.Generic.Dictionary<string, PropertyMeta>(StringComparer.Ordinal);
        List<PropertyMeta> listed = GodotData.ReadList(
            target.GetPropertyList(),
            info => new PropertyMeta(
                GodotData.String(info, GodotData.NameKey),
                (Variant.Type)GodotData.Int(info, GodotData.TypeKey),
                (PropertyHint)GodotData.Int(info, GodotData.HintKey),
                GodotData.String(info, GodotData.HintStringKey),
                (PropertyUsageFlags)GodotData.Int(info, GodotData.UsageKey)));
        foreach (PropertyMeta meta in listed)
        {
            if ((meta.Usage & (PropertyUsageFlags.Group | PropertyUsageFlags.Subgroup | PropertyUsageFlags.Category)) == 0)
            {
                result[meta.Name] = meta;
            }
        }

        return result;
    }

    private static System.Collections.Generic.Dictionary<string, int> ReadSignals(GodotObject target)
    {
        var result = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string name, int count) in GodotData.ReadList(
                     target.GetSignalList(),
                     info => (GodotData.String(info, GodotData.NameKey), GodotData.Count(info, GodotData.ArgsKey))))
        {
            result[name] = count;
        }

        return result;
    }
}

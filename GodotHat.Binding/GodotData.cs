using Godot;
using Godot.Collections;

namespace GodotHat.Binding;

// Reads Godot data without leaving Variants and collections for the finalizer thread. Each one holding a string,
// collection or object frees its native value from a finalizer, which can race Godot's shutdown and crash on exit.
internal static class GodotData
{
    public static readonly Variant NameKey = "name";
    public static readonly Variant TypeKey = "type";
    public static readonly Variant HintKey = "hint";
    public static readonly Variant HintStringKey = "hint_string";
    public static readonly Variant UsageKey = "usage";
    public static readonly Variant ArgsKey = "args";

    // The entries of a dictionary in a node's metadata, or null if the metadata isn't a dictionary.
    public static List<(string Key, GodotObject? Value)>? ReadMetaDictionary(Node node, StringName name)
    {
        using Variant meta = node.GetMeta(name);
        if (meta.VariantType != Variant.Type.Dictionary)
        {
            return null;
        }

        using Dictionary dictionary = meta.AsGodotDictionary();
        var entries = new List<(string Key, GodotObject? Value)>(dictionary.Count);
        foreach ((Variant key, Variant value) in dictionary)
        {
            using (key)
            using (value)
            {
                entries.Add((key.AsString(), value.AsGodotObject()));
            }
        }

        return entries;
    }

    // Reads each entry of a property, signal or method list.
    public static List<T> ReadList<T>(Array<Dictionary> list, Func<Dictionary, T> read)
    {
        using var entries = (Godot.Collections.Array)list;
        var result = new List<T>(entries.Count);
        foreach (Dictionary entry in list)
        {
            using (entry)
            {
                result.Add(read(entry));
            }
        }

        return result;
    }

    public static string String(Dictionary entry, Variant key)
    {
        using Variant value = entry[key];
        return value.AsString();
    }

    public static long Int(Dictionary entry, Variant key)
    {
        using Variant value = entry[key];
        return value.AsInt64();
    }

    public static int Count(Dictionary entry, Variant key)
    {
        using Variant value = entry[key];
        using Godot.Collections.Array array = value.AsGodotArray();
        return array.Count;
    }
}

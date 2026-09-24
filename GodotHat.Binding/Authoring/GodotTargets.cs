using System.Reflection;
using Godot;

namespace GodotHat.Binding;

/// <summary>
/// Describes Godot objects' properties for <see cref="TargetRules"/> and <see cref="PathSuggester"/>. For editor tooling
/// and validation; uses reflection to read custom Controls' <c>[NotBindable]</c> and <c>[BindableTarget]</c>.
/// </summary>
public static class GodotTargets
{
    /// <summary>Project setting holding extra <c>Class.property</c> patterns that can't be bound.</summary>
    public const string DenySetting = "godothat_binding/rules/deny";

    /// <summary>Project setting holding <c>Class.property</c> patterns to allow despite the default rules.</summary>
    public const string AllowSetting = "godothat_binding/rules/allow";

    /// <summary>The target rules, with the project's patterns from <see cref="DenySetting"/> and <see cref="AllowSetting"/>.</summary>
    public static TargetRules ProjectRules() =>
        new(ReadPatterns(DenySetting), ReadPatterns(AllowSetting));

    /// <summary>
    /// The classes <paramref name="target"/> derives from, most derived first: its script's C# and global class names,
    /// then its Godot classes.
    /// </summary>
    public static IReadOnlyList<string> ClassChain(GodotObject target)
    {
        var chain = new List<string>();
        for (Type? type = target.GetType(); type is not null && type.Assembly != typeof(GodotObject).Assembly; type = type.BaseType)
        {
            chain.Add(type.Name);
        }

        using Variant script = target.GetScript();
        if (script.Obj is Script scriptResource && scriptResource.GetGlobalName() is { IsEmpty: false } global && !chain.Contains(global))
        {
            chain.Add(global);
        }

        for (string godotClass = target.GetClass(); !string.IsNullOrEmpty(godotClass); godotClass = ClassDB.GetParentClass(godotClass))
        {
            chain.Add(godotClass);
        }

        return chain;
    }

    /// <summary>The bindable properties of <paramref name="target"/>, as Godot lists them, plus <c>[BindableTarget]</c> members.</summary>
    public static IEnumerable<TargetPropertyInfo> Properties(GodotObject target)
    {
        IReadOnlyList<string> chain = ClassChain(target);
        var seen = new HashSet<string>();
        List<(string Name, int Type, int Hint, long Usage)> listed = GodotData.ReadList(
            target.GetPropertyList(),
            info => (
                GodotData.String(info, GodotData.NameKey),
                (int)GodotData.Int(info, GodotData.TypeKey),
                (int)GodotData.Int(info, GodotData.HintKey),
                GodotData.Int(info, GodotData.UsageKey)));
        foreach ((string name, int type, int hint, long usage) in listed)
        {
            if (seen.Add(name))
            {
                yield return new TargetPropertyInfo(chain, name, type, hint, usage, MarkerOf(target, name));
            }
        }

        foreach (MemberInfo member in ScriptMembers(target))
        {
            if (member.IsDefined(typeof(BindableTargetAttribute)) && seen.Add(member.Name))
            {
                yield return new TargetPropertyInfo(chain, member.Name, 0, 0, 0, TargetMarker.BindableTarget);
            }
        }
    }

    /// <summary>Describes <paramref name="property"/> of <paramref name="target"/>, if it has one.</summary>
    public static bool TryGetProperty(GodotObject target, string property, out TargetPropertyInfo info)
    {
        foreach (TargetPropertyInfo candidate in Properties(target))
        {
            if (candidate.Name == property)
            {
                info = candidate;
                return true;
            }
        }

        info = default;
        return false;
    }

    /// <summary>The type of a target property, for <see cref="PathSuggester"/>.</summary>
    public static TargetTypeInfo TypeOf(in TargetPropertyInfo property) =>
        new(property.VariantType, property.Hint == (int)PropertyHint.Enum);

    private static TargetMarker MarkerOf(GodotObject target, string name)
    {
        foreach (MemberInfo member in ScriptMembers(target))
        {
            if (member.Name != name)
            {
                continue;
            }

            if (member.IsDefined(typeof(NotBindableAttribute)) || member.DeclaringType?.IsDefined(typeof(NotBindableAttribute), false) == true)
            {
                return TargetMarker.NotBindable;
            }

            return member.IsDefined(typeof(BindableTargetAttribute)) ? TargetMarker.BindableTarget : TargetMarker.None;
        }

        return TargetMarker.None;
    }

    private static IEnumerable<MemberInfo> ScriptMembers(GodotObject target) =>
        target.GetType().Assembly == typeof(GodotObject).Assembly
            ? []
            : target.GetType()
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m is PropertyInfo or FieldInfo && m.DeclaringType?.Assembly != typeof(GodotObject).Assembly);

    private static IEnumerable<string> ReadPatterns(string setting) =>
        ProjectSettings.HasSetting(setting) ? ProjectSettings.GetSetting(setting).AsStringArray() : [];
}

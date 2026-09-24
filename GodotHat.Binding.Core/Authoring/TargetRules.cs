namespace GodotHat.Binding;

/// <summary>How a custom Control marks a property for binding, with <c>[NotBindable]</c> or <c>[BindableTarget]</c>.</summary>
public enum TargetMarker
{
    /// <summary>No marker.</summary>
    None,

    /// <summary><c>[NotBindable]</c> on the property or its class: never a target.</summary>
    NotBindable,

    /// <summary><c>[BindableTarget]</c>: a target even though it isn't shown in the editor.</summary>
    BindableTarget,
}

/// <summary>A property of a Godot object, as the target rules see it.</summary>
/// <param name="ClassChain">The object's classes, most derived first, eg <c>Button, BaseButton, Control, ...</c>.</param>
/// <param name="Name">The property name.</param>
/// <param name="VariantType">Its Godot <c>Variant.Type</c>.</param>
/// <param name="Hint">Its Godot <c>PropertyHint</c>.</param>
/// <param name="Usage">Its Godot <c>PropertyUsageFlags</c>.</param>
/// <param name="Marker">Any marker from a custom Control's attributes.</param>
public readonly record struct TargetPropertyInfo(
    IReadOnlyList<string> ClassChain,
    string Name,
    int VariantType,
    int Hint,
    long Usage,
    TargetMarker Marker = TargetMarker.None);

/// <summary>
/// Which Control properties can be bound. Controls are bindable by default; rules then hide properties, in order, each
/// able to override the ones before: usage flags (internal, not shown in the editor, groups), the built-in denylist of
/// layout and editor noise, the project's deny and allow patterns, and custom Controls' <c>[NotBindable]</c> and
/// <c>[BindableTarget]</c> attributes. The editor and <c>BindingValidator</c> share these rules.
/// </summary>
/// <remarks>
/// Patterns are <c>Class.property</c> globs, where <c>*</c> matches anything and the class matches any class the object
/// derives from; <c>*.visible</c> or just <c>visible</c> matches every class.
/// </remarks>
public sealed class TargetRules
{
    private const long UsageEditor = 4;
    private const long UsageInternal = 8;
    private const long UsageGroup = 64;
    private const long UsageCategory = 128;
    private const long UsageSubgroup = 256;
    private const long UsageArray = 262144;

    private const int TypeObject = 24;
    private const int TypeNodePath = 22;
    private const int TypeRid = 23;
    private const int TypeCallable = 25;
    private const int TypeSignal = 26;
    private const int HintNodeType = 34;

    private readonly string[] deny;
    private readonly string[] allow;

    /// <summary>Creates rules with the project's own patterns on top of the defaults.</summary>
    public TargetRules(IEnumerable<string>? deny = null, IEnumerable<string>? allow = null)
    {
        this.deny = deny?.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray() ?? [];
        this.allow = allow?.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray() ?? [];
    }

    /// <summary>The default rules, with no project patterns.</summary>
    public static TargetRules Default { get; } = new();

    /// <summary>Layout, focus, processing, translation, accessibility and rendering settings that aren't useful to bind.</summary>
    public static IReadOnlyList<string> DefaultDenied { get; } =
    [
        "anchor_*", "anchors_preset", "offset_left", "offset_top", "offset_right", "offset_bottom", "layout_mode",
        "layout_direction", "size_flags_*", "grow_*", "pivot_offset*", "custom_maximum_size", "propagate_maximum_size",
        "focus_neighbor_*", "focus_next", "focus_previous", "process_*", "physics_interpolation_mode",
        "editor_description", "metadata/*", "shortcut_context", "auto_translate_mode", "tooltip_auto_translate_mode",
        "localize_numeral_system", "translation_context", "accessibility_*_nodes", "light_mask", "visibility_layer",
        "texture_filter", "texture_repeat", "clip_children", "top_level", "show_behind_parent", "z_as_relative",
        "y_sort_enabled", "use_parent_material", "oversampling_with_scale",
    ];

    /// <summary>Whether <paramref name="property"/> can be a binding target, and if not, why.</summary>
    public bool IsBindable(in TargetPropertyInfo property, out string reason)
    {
        bool shown = (property.Usage & UsageEditor) != 0 &&
                     (property.Usage & (UsageInternal | UsageGroup | UsageCategory | UsageSubgroup | UsageArray)) == 0;
        bool bindable = shown;
        reason = shown ? "" : "it isn't shown in the editor";

        if (bindable && DeniedByDefault(property, out string defaultReason))
        {
            bindable = false;
            reason = defaultReason;
        }

        if (bindable && Matches(this.deny, property))
        {
            bindable = false;
            reason = "the project's binding rules deny it";
        }
        else if (!bindable && shown && Matches(this.allow, property))
        {
            bindable = true;
            reason = "";
        }

        switch (property.Marker)
        {
            case TargetMarker.NotBindable:
                bindable = false;
                reason = "it is marked [NotBindable]";
                break;
            case TargetMarker.BindableTarget:
                bindable = true;
                reason = "";
                break;
        }

        return bindable;
    }

    private static bool DeniedByDefault(in TargetPropertyInfo property, out string reason)
    {
        if (property.VariantType is TypeNodePath or TypeRid or TypeCallable or TypeSignal ||
            (property.VariantType == TypeObject && property.Hint == HintNodeType))
        {
            reason = "node references, RIDs, callables and signals can't be bound";
            return true;
        }

        if (property.Name.Contains('/') && !property.Name.StartsWith("theme_override_", StringComparison.Ordinal) &&
            !property.Name.StartsWith("metadata/", StringComparison.Ordinal))
        {
            reason = "it is part of a list of items";
            return true;
        }

        if (Matches(DefaultDenied, property))
        {
            reason = "it is layout or editor configuration";
            return true;
        }

        reason = "";
        return false;
    }

    private static bool Matches(IReadOnlyList<string> patterns, in TargetPropertyInfo property)
    {
        foreach (string pattern in patterns)
        {
            int dot = pattern.IndexOf('.');
            string classPattern = dot < 0 ? "*" : pattern[..dot];
            string namePattern = dot < 0 ? pattern : pattern[(dot + 1)..];
            if (Glob(namePattern, property.Name) &&
                (classPattern == "*" || property.ClassChain.Any(c => Glob(classPattern, c))))
            {
                return true;
            }
        }

        return false;
    }

    // Matches text against a pattern where * matches any run of characters.
    internal static bool Glob(string pattern, string text)
    {
        int p = 0;
        int t = 0;
        int star = -1;
        int mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (p < pattern.Length && pattern[p] == text[t])
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}

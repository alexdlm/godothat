namespace GodotHat.Binding;

/// <summary>The broad type of a binding target, from its Godot <c>Variant.Type</c> and hint.</summary>
/// <param name="VariantType">The Godot <c>Variant.Type</c>; 0 (nil) takes any value.</param>
/// <param name="IsEnumHint">Whether it has an enum hint, eg <c>Control.mouse_filter</c>.</param>
public readonly record struct TargetTypeInfo(int VariantType, bool IsEnumHint = false)
{
    /// <summary>A bool property, eg <c>visible</c>.</summary>
    public static TargetTypeInfo Bool => new(1);

    /// <summary>An int property.</summary>
    public static TargetTypeInfo Int => new(2);

    /// <summary>A float property, eg <c>value</c>.</summary>
    public static TargetTypeInfo Float => new(3);

    /// <summary>A string property, eg <c>text</c>.</summary>
    public static TargetTypeInfo String => new(4);
}

/// <summary>How well a member suits a binding target.</summary>
public enum PathCompatibility
{
    /// <summary>Binds directly.</summary>
    Compatible,

    /// <summary>Binds through one of the suggested converters.</summary>
    NeedsConverter,

    /// <summary>Can't be bound to this target.</summary>
    Incompatible,
}

/// <summary>A path a binding could use, with how well it suits the target.</summary>
/// <param name="Path">The dotted path from the data context.</param>
/// <param name="Member">The member at the end of the path.</param>
/// <param name="Compatibility">How well it suits the target.</param>
/// <param name="Converters">Converters that would make it fit, best first.</param>
public sealed record PathSuggestion(
    string Path,
    MemberAccessor Member,
    PathCompatibility Compatibility,
    IReadOnlyList<string> Converters);

/// <summary>
/// Lists the paths a binding could use from a view model, compatible and shallow ones first, suggesting converters where the types
/// differ: eg an int into <c>text</c> suggests <c>to_string</c> or <c>format</c>, an object into <c>visible</c> suggests
/// <c>not_null</c>. Used by the editor's path picker.
/// </summary>
public static class PathSuggester
{
    private const int TypeNil = 0;
    private const int TypeBool = 1;
    private const int TypeInt = 2;
    private const int TypeFloat = 3;
    private const int TypeString = 4;
    private const int TypeStringName = 21;
    private const int TypeObject = 24;

    /// <summary>Suggests paths from <paramref name="contextType"/> for a binding of <paramref name="kind"/>.</summary>
    /// <param name="contextType">The data context's declared type.</param>
    /// <param name="kind">The binding kind.</param>
    /// <param name="target">The target property's type, for property bindings.</param>
    /// <param name="maxDepth">How many view model hops to follow; this also bounds view models that refer to their own type.</param>
    public static IReadOnlyList<PathSuggestion> Suggest(Type contextType, BindingKind kind, TargetTypeInfo target = default, int maxDepth = 2)
    {
        var suggestions = new List<PathSuggestion>();
        Walk(contextType, "", kind, target, maxDepth, suggestions);
        return suggestions
            .Select((s, i) => (s, i))
            .OrderBy(x => x.s.Compatibility)
            .ThenBy(x => x.s.Path.Count(c => c == '.'))
            .ThenBy(x => x.i)
            .Select(x => x.s)
            .ToList();
    }

    /// <summary>How well values of <paramref name="valueType"/> suit <paramref name="target"/>, and the converters that would help.</summary>
    public static PathCompatibility Check(Type valueType, bool isCollection, TargetTypeInfo target, out IReadOnlyList<string> converters)
    {
        converters = [];
        if (isCollection)
        {
            switch (target.VariantType)
            {
                case TypeNil or TypeInt or TypeFloat:
                    return PathCompatibility.Compatible;
                case TypeBool:
                    converters = [BuiltInConverters.AnyId];
                    return PathCompatibility.NeedsConverter;
                case TypeString or TypeStringName:
                    converters = [BuiltInConverters.FormatId, BuiltInConverters.ToStringId];
                    return PathCompatibility.NeedsConverter;
                default:
                    return PathCompatibility.Incompatible;
            }
        }

        Type type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        bool isNumeric = type.IsPrimitive && type != typeof(bool) || type.IsEnum || type == typeof(decimal);
        switch (target.VariantType)
        {
            case TypeNil:
                return PathCompatibility.Compatible;
            case TypeBool when type == typeof(bool):
            case TypeInt or TypeFloat when isNumeric || type == typeof(bool):
                if (target.IsEnumHint && type.IsEnum)
                {
                    converters = [BuiltInConverters.EnumNameId];
                }

                return PathCompatibility.Compatible;
            case TypeBool when !type.IsValueType || Nullable.GetUnderlyingType(valueType) is not null:
                converters = [BuiltInConverters.NotNullId, BuiltInConverters.IsNullId];
                return PathCompatibility.NeedsConverter;
            case TypeBool when type == typeof(int):
                converters = [BuiltInConverters.AnyId];
                return PathCompatibility.NeedsConverter;
            case TypeString or TypeStringName when type == typeof(string) || type.FullName is "Godot.StringName" or "Godot.NodePath":
                return PathCompatibility.Compatible;
            case TypeString or TypeStringName:
                converters = type.IsEnum
                    ? [BuiltInConverters.EnumNameId, BuiltInConverters.ToStringId]
                    : [BuiltInConverters.ToStringId, BuiltInConverters.FormatId];
                return PathCompatibility.NeedsConverter;
            case TypeObject when IsGodotObject(type):
                return PathCompatibility.Compatible;
            case var other when other == VariantTypeOf(type):
                return PathCompatibility.Compatible;
            default:
                return PathCompatibility.Incompatible;
        }
    }

    private static void Walk(
        Type ownerType,
        string prefix,
        BindingKind kind,
        TargetTypeInfo target,
        int depth,
        List<PathSuggestion> suggestions)
    {
        if (depth < 0 || !BindingRegistry.TryGet(ownerType, out ViewModelAccessor? accessor))
        {
            return;
        }

        foreach (MemberAccessor member in accessor.Members)
        {
            string path = prefix + member.Name;
            PathCompatibility compatibility = CompatibilityOf(member, kind, target, out IReadOnlyList<string> converters);
            suggestions.Add(new PathSuggestion(path, member, compatibility, converters));

            if (member is ValueMember && !member.ValueType.IsValueType && BindingRegistry.TryGet(member.ValueType, out _))
            {
                Walk(member.ValueType, path + ".", kind, target, depth - 1, suggestions);
            }
        }
    }

    private static PathCompatibility CompatibilityOf(MemberAccessor member, BindingKind kind, TargetTypeInfo target, out IReadOnlyList<string> converters)
    {
        converters = [];
        return kind switch
        {
            BindingKind.Command => member.Kind == MemberKind.Command ? PathCompatibility.Compatible : PathCompatibility.Incompatible,
            BindingKind.Items => member.Kind == MemberKind.Items ? PathCompatibility.Compatible : PathCompatibility.Incompatible,
            BindingKind.Context => member is ValueMember && !member.ValueType.IsValueType ? PathCompatibility.Compatible : PathCompatibility.Incompatible,
            BindingKind.Event => member is ValueMember ? PathCompatibility.Compatible : PathCompatibility.Incompatible,
            _ when member.Kind == MemberKind.Command => PathCompatibility.Incompatible,
            _ => Check(member.ValueType, member.Kind == MemberKind.Items, target, out converters),
        };
    }

    private static bool IsGodotObject(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.FullName == "Godot.GodotObject")
            {
                return true;
            }
        }

        return false;
    }

    // Godot structs, by name, as Core doesn't reference Godot
    private static int VariantTypeOf(Type type) => type.FullName switch
    {
        "Godot.Vector2" => 5,
        "Godot.Vector2I" => 6,
        "Godot.Rect2" => 7,
        "Godot.Rect2I" => 8,
        "Godot.Vector3" => 9,
        "Godot.Vector3I" => 10,
        "Godot.Transform2D" => 11,
        "Godot.Vector4" => 12,
        "Godot.Vector4I" => 13,
        "Godot.Color" => 20,
        "Godot.StringName" => 21,
        "Godot.NodePath" => 22,
        _ => -1,
    };
}

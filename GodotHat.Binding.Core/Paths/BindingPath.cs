using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using R3;

namespace GodotHat.Binding;

/// <summary>
/// A dotted member path resolved against a view model type's accessors: zero or more view model hops, then the
/// member being bound. Compiled paths are cached per type and path.
/// </summary>
public sealed class BindingPath
{
    private static readonly ConcurrentDictionary<(Type, string), BindingPath> cache = new();

    private readonly ValueMember[] hops;

    private BindingPath(Type contextType, string text, ValueMember[] hops, MemberAccessor? leaf)
    {
        this.ContextType = contextType;
        this.Text = text;
        this.hops = hops;
        this.Leaf = leaf;
    }

    /// <summary>The declared context type the path starts from.</summary>
    public Type ContextType { get; }

    /// <summary>The path as written.</summary>
    public string Text { get; }

    /// <summary>The members leading to the view model that owns <see cref="Leaf"/>.</summary>
    public IReadOnlyList<ValueMember> Hops => this.hops;

    /// <summary>The bound member, or null when the path is empty and binds the context itself.</summary>
    public MemberAccessor? Leaf { get; }

    /// <summary>Resolves <paramref name="path"/> against <paramref name="contextType"/>.</summary>
    public static bool TryCompile(
        Type contextType,
        string path,
        [NotNullWhen(true)] out BindingPath? compiled,
        [NotNullWhen(false)] out string? error)
    {
        if (cache.TryGetValue((contextType, path), out compiled))
        {
            error = null;
            return true;
        }

        if (!TryResolve(contextType, path, out compiled, out error))
        {
            return false;
        }

        compiled = cache.GetOrAdd((contextType, path), compiled);
        return true;
    }

    // The owners of the leaf member, following the hops from each context value. Null when a hop can't be resolved.
    internal Observable<object?> ObserveOwners(Observable<object?> context)
    {
        Observable<object?> owners = context;
        foreach (ValueMember hop in this.hops)
        {
            owners = hop.SwitchOwners(owners);
        }

        return owners;
    }

    /// <inheritdoc />
    public override string ToString() => $"{this.ContextType.Name}.{this.Text}";

    private static bool TryResolve(
        Type contextType,
        string path,
        [NotNullWhen(true)] out BindingPath? compiled,
        [NotNullWhen(false)] out string? error)
    {
        compiled = null;
        string trimmed = path.Trim();
        if (trimmed.Length == 0)
        {
            compiled = new BindingPath(contextType, path, [], null);
            error = null;
            return true;
        }

        string[] segments = trimmed.Split('.');
        var hops = new ValueMember[segments.Length - 1];
        Type ownerType = contextType;
        MemberAccessor? member = null;

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i].Trim();
            if (segment.Length == 0)
            {
                error = $"Path '{path}' has an empty segment.";
                return false;
            }

            if (!BindingRegistry.TryGet(ownerType, out ViewModelAccessor? accessor))
            {
                error = i == 0
                    ? $"{ownerType.FullName} is not a [ViewModel]."
                    : $"'{segments[i - 1]}' is a {ownerType.FullName}, which is not a [ViewModel].";
                return false;
            }

            if (!accessor.TryGetMember(segment, out member))
            {
                error = $"{accessor.ViewModelType.Name} has no bindable member '{segment}'.";
                return false;
            }

            if (i < segments.Length - 1)
            {
                if (member is not ValueMember hop || member.ValueType.IsValueType)
                {
                    error = $"Path '{path}' can't continue past {accessor.ViewModelType.Name}.{segment}, which is not a view model.";
                    return false;
                }

                hops[i] = hop;
                ownerType = member.ValueType;
            }
        }

        compiled = new BindingPath(contextType, path, hops, member);
        error = null;
        return true;
    }
}

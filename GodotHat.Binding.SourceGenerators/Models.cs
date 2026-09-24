using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace GodotHat.Binding.SourceGenerators;

// Generator models hold no symbols or syntax, only values with value equality, so unchanged view models are cached
// between runs.

internal enum MemberCategory
{
    ReactiveProperty,
    ReadOnlyProperty,
    BindableProperty,
    ReadOnlyBindableProperty,
    Observable,
    Command,
    Constant,
    Collection,
    View,
}

// For views, ValueType is the view type and SourceType the collection's item type.
internal sealed record MemberModel(string Name, MemberCategory Category, string ValueType, string? SourceType = null);

internal sealed record EnumModel(string Type, EquatableArray<string> Members);

internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(this.FilePath, this.Span, this.LineSpan);

    public static LocationInfo? From(ISymbol symbol)
    {
        Location? location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location?.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public Diagnostic ToDiagnostic() => Diagnostic.Create(
        BindingDiagnostics.ById[this.Id],
        this.Location?.ToLocation(),
        this.Arguments.Cast<object>().ToArray());
}

internal enum ParameterSource
{
    Required,
    Optional,
    ServiceProvider,
}

internal sealed record ParameterModel(string Type, ParameterSource Source);

// ServiceParameters is null when the view model can't be created from a service provider.
internal sealed record ViewModelModel(
    string TypeName,
    string HintName,
    string GeneratedClassName,
    bool CanCreate,
    bool Emit,
    EquatableArray<MemberModel> Members,
    EquatableArray<EnumModel> Enums,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<ParameterModel>? ServiceParameters = null);

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> items;

    public EquatableArray(ImmutableArray<T> items)
    {
        this.items = items;
    }

    public int Count => this.items.IsDefault ? 0 : this.items.Length;

    public bool Equals(EquatableArray<T> other) =>
        this.Count == other.Count && (this.Count == 0 || this.items.SequenceEqual(other.items));

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && this.Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (T item in this)
        {
            hash = unchecked((hash * 31) + item.GetHashCode());
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() =>
        ((IEnumerable<T>)(this.items.IsDefault ? ImmutableArray<T>.Empty : this.items)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T> =>
        new(items.ToImmutableArray());
}

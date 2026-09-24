using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace GodotHat.Binding;

/// <summary>Converters by id, as referenced by bindings.</summary>
/// <example>
/// <code>
/// BindingConverterRegistry.Default.Register("percent", BindingConverter.Create&lt;float, string&gt;(v => $"{v:P0}"));
/// </code>
/// </example>
public sealed class BindingConverterRegistry
{
    private readonly ConcurrentDictionary<string, IBindingConverter> converters = new(StringComparer.Ordinal);

    /// <summary>The registry used by bindings unless another is supplied, with the built-in converters.</summary>
    public static BindingConverterRegistry Default { get; } = CreateWithBuiltIns();

    /// <summary>Registered converter ids.</summary>
    public ICollection<string> Ids => this.converters.Keys;

    /// <summary>
    /// Creates a registry with the built-in converters: <c>not</c> (alias <c>invert</c>), <c>not_null</c>,
    /// <c>is_null</c>, <c>to_string</c>, <c>format</c>, <c>equals</c>, <c>enum_name</c>, <c>count</c> and <c>any</c>.
    /// </summary>
    public static BindingConverterRegistry CreateWithBuiltIns()
    {
        var registry = new BindingConverterRegistry();
        registry.Register(BuiltInConverters.NotId, BuiltInConverters.NotConverter.Instance);
        registry.Register(BuiltInConverters.InvertId, BuiltInConverters.NotConverter.Instance);
        registry.Register(BuiltInConverters.NotNullId, BuiltInConverters.NullConverter.NotNull);
        registry.Register(BuiltInConverters.IsNullId, BuiltInConverters.NullConverter.IsNull);
        registry.Register(BuiltInConverters.ToStringId, BuiltInConverters.ToStringConverter.Instance);
        registry.Register(BuiltInConverters.FormatId, BuiltInConverters.FormatConverter.Instance);
        registry.Register(BuiltInConverters.EqualsId, BuiltInConverters.EqualsConverter.Instance);
        registry.Register(BuiltInConverters.EnumNameId, BuiltInConverters.EnumNameConverter.Instance);
        registry.Register(BuiltInConverters.CountId, BuiltInConverters.CountConverter.Count);
        registry.Register(BuiltInConverters.AnyId, BuiltInConverters.CountConverter.Any);
        return registry;
    }

    /// <summary>Registers a converter, replacing any with the same id.</summary>
    public void Register(string id, IBindingConverter converter) => this.converters[id] = converter;

    /// <summary>Finds a converter by id.</summary>
    public bool TryGet(string id, [NotNullWhen(true)] out IBindingConverter? converter) =>
        this.converters.TryGetValue(id, out converter);
}

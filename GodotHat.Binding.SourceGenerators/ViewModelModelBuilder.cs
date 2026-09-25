using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using GodotHat.SourceGenerators;

namespace GodotHat.Binding.SourceGenerators;

// Reads a [ViewModel] class into a model: its bindable members, classified by type, and the enums they use.
internal static class ViewModelModelBuilder
{
    public const string ViewModelAttributeName = "GodotHat.Binding.ViewModelAttribute";
    private const string NotBindableAttributeName = "GodotHat.Binding.NotBindableAttribute";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static ViewModelModel Build(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        var known = new KnownTypes(compilation);
        var diagnostics = new List<DiagnosticInfo>();
        string typeName = type.ToDisplayString(TypeFormat);
        string hintName = GeneratorUtil.GetUniqueHintName(type);
        string generatedClassName = "ViewModelAccessor_" + new string(hintName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        if (IsGeneric(type))
        {
            diagnostics.Add(Diagnostic(BindingDiagnostics.GenericViewModel, type, type.Name));
            return Empty(false);
        }

        if (!IsAccessible(type))
        {
            diagnostics.Add(Diagnostic(BindingDiagnostics.InaccessibleViewModel, type, type.Name));
            return Empty(false);
        }

        var members = new List<(int Depth, MemberModel Member, ITypeSymbol ValueType)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int depth = 0;
        for (INamedTypeSymbol? current = type;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType, depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only follow bases in this compilation, or other view models, so eg Godot.Node's properties aren't bound.
            if (depth > 0 &&
                !SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, type.ContainingAssembly) &&
                !HasAttribute(current, known.ViewModelAttribute))
            {
                break;
            }

            foreach (IPropertySymbol property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic ||
                    property.IsIndexer ||
                    property.DeclaredAccessibility != Accessibility.Public ||
                    property.GetMethod?.DeclaredAccessibility != Accessibility.Public ||
                    !seen.Add(property.Name) ||
                    HasAttribute(property, known.NotBindableAttribute))
                {
                    continue;
                }

                if (Implementation(property.Type, known.SynchronizedViewList) is not null)
                {
                    diagnostics.Add(Diagnostic(BindingDiagnostics.ViewListNotBindable, property, type.Name, property.Name));
                    continue;
                }

                if (!TryClassify(property.Type, known, out MemberCategory category, out ITypeSymbol valueType, out ITypeSymbol? sourceType))
                {
                    continue;
                }

                if (category != MemberCategory.Constant && property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false })
                {
                    diagnostics.Add(Diagnostic(BindingDiagnostics.ReactiveMemberHasSetter, property, type.Name, property.Name));
                }

                if (category is not (MemberCategory.Command or MemberCategory.Constant or MemberCategory.Collection or MemberCategory.View) &&
                    !IsVariantCompatible(valueType, known))
                {
                    diagnostics.Add(Diagnostic(
                        BindingDiagnostics.NoVariantConversion,
                        property,
                        type.Name,
                        property.Name,
                        valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }

                members.Add((
                    depth,
                    new MemberModel(property.Name, category, valueType.ToDisplayString(TypeFormat), sourceType?.ToDisplayString(TypeFormat)),
                    valueType));
            }
        }

        // Base class members first, then each subclass's, in declaration order.
        List<(int Depth, MemberModel Member, ITypeSymbol ValueType)> ordered = members
            .Select((m, index) => (m, index))
            .OrderByDescending(x => x.m.Depth)
            .ThenBy(x => x.index)
            .Select(x => x.m)
            .ToList();

        foreach (IGrouping<string, MemberModel> clash in ordered
                     .Select(m => m.Member)
                     .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            MemberModel[] names = clash.ToArray();
            diagnostics.Add(Diagnostic(BindingDiagnostics.AmbiguousMemberNames, type, type.Name, names[0].Name, names[1].Name));
        }

        EnumModel[] enums = ordered
            .Select(m => m.ValueType)
            .OfType<INamedTypeSymbol>()
            .Where(t => t.TypeKind == TypeKind.Enum && IsAccessible(t))
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .Select(t => new EnumModel(
                t.ToDisplayString(TypeFormat),
                t.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).Select(f => f.Name).ToEquatableArray()))
            .ToArray();

        return new ViewModelModel(
            typeName,
            hintName,
            generatedClassName,
            CreatableConstructors(type, known).Any(c => c.Parameters.IsEmpty),
            true,
            ordered.Select(m => m.Member).ToEquatableArray(),
            enums.ToEquatableArray(),
            diagnostics.ToEquatableArray(),
            ServiceParameters(type, known));

        ViewModelModel Empty(bool emit) => new(
            typeName,
            hintName,
            generatedClassName,
            false,
            emit,
            default,
            default,
            diagnostics.ToEquatableArray());
    }

    // Classifies a member by its type, most specific first: its base class chain is searched from the declared type
    // up, so eg a ReactiveProperty<T> matches before the ReadOnlyReactiveProperty<T> and Observable<T> it derives from.
    private static bool TryClassify(
        ITypeSymbol type,
        KnownTypes known,
        out MemberCategory category,
        out ITypeSymbol valueType,
        out ITypeSymbol? sourceType)
    {
        sourceType = null;
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current is not INamedTypeSymbol { IsGenericType: true } named)
            {
                continue;
            }

            INamedTypeSymbol definition = named.OriginalDefinition;
            MemberCategory? match =
                Is(definition, known.ReactiveProperty) ? MemberCategory.ReactiveProperty :
                Is(definition, known.ReadOnlyReactiveProperty) ? MemberCategory.ReadOnlyProperty :
                Is(definition, known.ReactiveCommand2) || Is(definition, known.ReactiveCommand1) ? MemberCategory.Command :
                Is(definition, known.Observable) ? MemberCategory.Observable :
                null;

            if (match is { } found)
            {
                category = found;
                valueType = named.TypeArguments[0];
                return true;
            }
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Interface } @interface)
        {
            foreach (INamedTypeSymbol candidate in new[] { @interface }.Concat(@interface.AllInterfaces))
            {
                if (Is(candidate.OriginalDefinition, known.BindableReactiveProperty))
                {
                    category = MemberCategory.BindableProperty;
                    valueType = candidate.TypeArguments[0];
                    return true;
                }
            }

            foreach (INamedTypeSymbol candidate in new[] { @interface }.Concat(@interface.AllInterfaces))
            {
                if (Is(candidate.OriginalDefinition, known.ReadOnlyBindableReactiveProperty))
                {
                    category = MemberCategory.ReadOnlyBindableProperty;
                    valueType = candidate.TypeArguments[0];
                    return true;
                }
            }
        }

        if (Implementation(type, known.SynchronizedView) is { } view)
        {
            category = MemberCategory.View;
            sourceType = view.TypeArguments[0];
            valueType = view.TypeArguments[1];
            return true;
        }

        if (Implementation(type, known.ObservableCollection) is { } collection)
        {
            category = MemberCategory.Collection;
            valueType = collection.TypeArguments[0];
            return true;
        }

        category = MemberCategory.Constant;
        valueType = type;
        return IsViewModel(type, known) || GodotSourceGeneratorsUtil.GetGodotType(type) is not null;
    }

    // The constructed interface `definition` that `type` is or implements, if any.
    private static INamedTypeSymbol? Implementation(ITypeSymbol type, INamedTypeSymbol? definition)
    {
        if (definition is null)
        {
            return null;
        }

        if (type is INamedTypeSymbol named && Is(named.OriginalDefinition, definition))
        {
            return named;
        }

        return type.AllInterfaces.FirstOrDefault(i => Is(i.OriginalDefinition, definition));
    }

    private static bool IsVariantCompatible(ITypeSymbol type, KnownTypes known) =>
        GodotSourceGeneratorsUtil.GetGodotType(type) is not null ||
        IsViewModel(type, known) ||
        Is(type, known.Unit);

    private static bool IsViewModel(ITypeSymbol type, KnownTypes known)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (HasAttribute(current, known.ViewModelAttribute))
            {
                return true;
            }
        }

        return false;
    }

    // Constructors generated code can call: accessible, and setting any required members. Nodes are never activated.
    private static IEnumerable<IMethodSymbol> CreatableConstructors(INamedTypeSymbol type, KnownTypes known)
    {
        if (type.IsAbstract || DerivesFrom(type, known.GodotObject))
        {
            return [];
        }

        bool hasRequiredMembers = GeneratorUtil.GetThisAndBaseTypes(type)
            .SelectMany(t => t.GetMembers())
            .Any(m => m is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true });

        return type.InstanceConstructors.Where(c =>
            c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal &&
            c.Parameters.All(p => p.RefKind == RefKind.None && !p.IsParams) &&
            (!hasRequiredMembers || c.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute")));
    }

    // Parameters of the constructor with the most required parameters, resolved from a service provider. Optional
    // parameters keep their defaults.
    private static EquatableArray<ParameterModel>? ServiceParameters(INamedTypeSymbol type, KnownTypes known)
    {
        IMethodSymbol? constructor = CreatableConstructors(type, known)
            .OrderByDescending(c => c.Parameters.Count(p => !p.HasExplicitDefaultValue))
            .FirstOrDefault();
        if (constructor is null)
        {
            return null;
        }

        return constructor.Parameters
            .TakeWhile(p => !p.HasExplicitDefaultValue)
            .Select(p => new ParameterModel(
                p.Type.ToDisplayString(TypeFormat),
                Is(p.Type, known.ServiceProvider) ? ParameterSource.ServiceProvider
                : p.NullableAnnotation == NullableAnnotation.Annotated || p.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? ParameterSource.Optional
                : ParameterSource.Required))
            .ToEquatableArray();
    }

    private static bool IsGeneric(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol? baseType) =>
        baseType is not null && GeneratorUtil.GetThisAndBaseTypes(type).Any(t => Is(t, baseType));

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute) =>
        attribute is not null && symbol.GetAttributes().Any(a => Is(a.AttributeClass, attribute));

    private static bool Is(ITypeSymbol? type, INamedTypeSymbol? known) =>
        known is not null && SymbolEqualityComparer.Default.Equals(type, known);

    private static DiagnosticInfo Diagnostic(DiagnosticDescriptor descriptor, ISymbol symbol, params string[] arguments) =>
        new(descriptor.Id, LocationInfo.From(symbol), arguments.ToEquatableArray());

    private sealed class KnownTypes(Compilation compilation)
    {
        public INamedTypeSymbol? ViewModelAttribute { get; } = compilation.GetTypeByMetadataName(ViewModelAttributeName);
        public INamedTypeSymbol? NotBindableAttribute { get; } = compilation.GetTypeByMetadataName(NotBindableAttributeName);
        public INamedTypeSymbol? ReactiveProperty { get; } = compilation.GetTypeByMetadataName("R3.ReactiveProperty`1");
        public INamedTypeSymbol? ReadOnlyReactiveProperty { get; } = compilation.GetTypeByMetadataName("R3.ReadOnlyReactiveProperty`1");
        public INamedTypeSymbol? BindableReactiveProperty { get; } = compilation.GetTypeByMetadataName("R3.IBindableReactiveProperty`1");
        public INamedTypeSymbol? ReadOnlyBindableReactiveProperty { get; } = compilation.GetTypeByMetadataName("R3.IReadOnlyBindableReactiveProperty`1");
        public INamedTypeSymbol? ReactiveCommand1 { get; } = compilation.GetTypeByMetadataName("R3.ReactiveCommand`1");
        public INamedTypeSymbol? ReactiveCommand2 { get; } = compilation.GetTypeByMetadataName("R3.ReactiveCommand`2");
        public INamedTypeSymbol? Observable { get; } = compilation.GetTypeByMetadataName("R3.Observable`1");
        public INamedTypeSymbol? Unit { get; } = compilation.GetTypeByMetadataName("R3.Unit");
        public INamedTypeSymbol? GodotObject { get; } = compilation.GetTypeByMetadataName("Godot.GodotObject");
        public INamedTypeSymbol? ServiceProvider { get; } = compilation.GetTypeByMetadataName("System.IServiceProvider");
        public INamedTypeSymbol? ObservableCollection { get; } = compilation.GetTypeByMetadataName("ObservableCollections.IObservableCollection`1");
        public INamedTypeSymbol? SynchronizedView { get; } = compilation.GetTypeByMetadataName("ObservableCollections.ISynchronizedView`2");
        public INamedTypeSymbol? SynchronizedViewList { get; } = compilation.GetTypeByMetadataName("ObservableCollections.ISynchronizedViewList`1");
    }

    internal static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;
}

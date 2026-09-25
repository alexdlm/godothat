using Microsoft.CodeAnalysis;

namespace GodotHat.Binding.SourceGenerators;

internal static class BindingDiagnostics
{
    private const string Category = "GodotHat.Binding";

    public static readonly DiagnosticDescriptor ReactiveMemberHasSetter = new(
        "GHB0001",
        "Reactive view model member has a public setter",
        "'{0}.{1}' is a reactive member with a public setter; replacing it doesn't update existing bindings, so make it get-only",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor NoVariantConversion = new(
        "GHB0002",
        "View model member type has no Godot Variant conversion",
        "'{0}.{1}' has values of type {2}, which can't be passed to Godot directly; bind it with a converter such as to_string or format",
        Category,
        DiagnosticSeverity.Info,
        true);

    public static readonly DiagnosticDescriptor AmbiguousMemberNames = new(
        "GHB0003",
        "View model members differ only by case",
        "'{0}' has bindable members '{1}' and '{2}' whose names differ only by case, which is easy to confuse in binding paths",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor InaccessibleViewModel = new(
        "GHB0004",
        "View model is not accessible",
        "View model '{0}' must be accessible from its assembly, not private or protected, for its accessor to be generated",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor GenericViewModel = new(
        "GHB0005",
        "Generic view models are not supported",
        "View model '{0}' is generic, so no accessor is generated; mark a non-generic subclass [ViewModel] instead",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor ViewListNotBindable = new(
        "GHB0006",
        "View lists can't be bound",
        "'{0}.{1}' is a view list, which can't be bound as items; expose the collection or its ISynchronizedView instead",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> ById = new[]
    {
        ReactiveMemberHasSetter,
        NoVariantConversion,
        AmbiguousMemberNames,
        InaccessibleViewModel,
        GenericViewModel,
        ViewListNotBindable,
    }.ToDictionary(d => d.Id);
}

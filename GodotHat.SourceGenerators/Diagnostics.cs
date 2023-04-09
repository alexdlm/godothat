using Microsoft.CodeAnalysis;

namespace GodotHat.SourceGenerators;

internal static class Diagnostics
{
    // ReSharper disable InconsistentNaming
    private const string ID_GH0001 = "GH0001";
    private const string ID_GH0002 = "GH0002";
    private const string ID_GH0003 = "GH0003";
    private const string ID_GH0004 = "GH0004";
    private const string ID_GH0005 = "GH0005";
    private const string ID_GH0006 = "GH0006";
    // ReSharper restore InconsistentNaming

    public static Diagnostic CreateNodeNotPartial(INamedTypeSymbol classSymbol) => Diagnostic.Create(
        new DiagnosticDescriptor(
            ID_GH0001,
            "Node class with GodotHat attributes is not partial",
            " {0} class declaration should have partial modifier so source can be generated.",
            "GodotHat.generation",
            DiagnosticSeverity.Warning,
            true),
        classSymbol.Locations.FirstOrDefault(),
        classSymbol.Name,
        classSymbol.ContainingNamespace.ToString());

    public static Diagnostic CreateNodeAlreadyContainsMethod(INamedTypeSymbol classSymbol, string overrideEventFunctionName, string attributeShortName) => Diagnostic.Create(
        new DiagnosticDescriptor(
            ID_GH0002,
            "Node class with GodotHat attributes already implements Godot override",
            $" {{0}} class declaration should not have a {overrideEventFunctionName} " +
            $"function defined, so it can be generated instead. Use [{attributeShortName}] attribute " +
            $"on an method instead, or remove the other [{attributeShortName}] members.",
            "GodotHat.generation",
            DiagnosticSeverity.Warning,
            true),
        classSymbol.Locations.FirstOrDefault(),
        classSymbol.Name,
        classSymbol.ContainingNamespace.ToString());

    public static Diagnostic CreateMethodShouldHaveNoParams(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol attribute,
        IMethodSymbol method) =>
        Diagnostic.Create(
            new DiagnosticDescriptor(
                ID_GH0003,
                $"Method with attribute [{attribute.Name}] must not take any parameters.",
                $" {classSymbol.Name}.{method.Name} method declaration should have an empty parameter list.",
                "GodotHat.generation",
                DiagnosticSeverity.Warning,
                true),
            classSymbol.Locations.FirstOrDefault());

    public static Diagnostic CreateMethodShouldReturnVoid(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol attribute,
        IMethodSymbol method) =>
        Diagnostic.Create(
            new DiagnosticDescriptor(
                ID_GH0004,
                $"Method with attribute [{attribute.Name}] should return void.",
                $" {classSymbol.Name}.{method.Name} should return void.",
                "GodotHat.generation",
                DiagnosticSeverity.Warning,
                true),
            classSymbol.Locations.FirstOrDefault());

    public static Diagnostic CreateMethodShouldBePrivate(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol attribute,
        IMethodSymbol method) =>
        Diagnostic.Create(
            new DiagnosticDescriptor(
                ID_GH0005,
                $"Method with attribute [{attribute.Name}] should be private.",
                $" {classSymbol.Name}.{method.Name} should be private. The generated Update{method.Name} method will be public",
                "GodotHat.generation",
                DiagnosticSeverity.Warning,
                true),
            classSymbol.Locations.FirstOrDefault());

    public static Diagnostic CreateMethodShouldReturnIDisposable(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol attribute,
        IMethodSymbol method) =>
        Diagnostic.Create(
            new DiagnosticDescriptor(
                ID_GH0006,
                $"Method with attribute [{attribute.Name}] should return IDispos.",
                $" {classSymbol.Name}.{method.Name} should return IDisposable (or IDisposable?).",
                "GodotHat.generation",
                DiagnosticSeverity.Warning,
                true),
            classSymbol.Locations.FirstOrDefault());
}

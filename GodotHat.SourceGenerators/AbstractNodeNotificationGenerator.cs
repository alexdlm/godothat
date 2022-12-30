using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotHat.SourceGenerators;

public abstract class AbstractNodeNotificationGenerator : IIncrementalGenerator
{
    protected abstract string AttributeFullName { get; }
    protected abstract string AttributeShortName { get; }
    protected abstract string OverrideEventFunctionName { get; }
    protected abstract bool AllowDisposableReturns { get; }

    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<ClassToProcess>> nodeTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                this.GetNode)
            .Where(n => n is not null)
            .Collect()!;

        context.RegisterSourceOutput(nodeTypes, this.GenerateNodeAdditions);
    }

    protected virtual ClassToProcess? GetNode(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        INamedTypeSymbol typeNodeClass = GetRequiredType(context.SemanticModel, "Godot.Node");
        INamedTypeSymbol typeAttribute = GetRequiredType(context.SemanticModel, this.AttributeFullName);
        INamedTypeSymbol typeIDisposable = GetRequiredType(context.SemanticModel, "System.IDisposable");
        INamedTypeSymbol typeAutoDisposeAttribute =
            GetRequiredType(context.SemanticModel, "GodotHat.AutoDisposeAttribute");

        var classSyntaxNode = (ClassDeclarationSyntax)context.Node;
        bool isPartial = classSyntaxNode.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        var diagnostics = new List<Diagnostic>();

        INamedTypeSymbol? classSymbol = context.SemanticModel.GetDeclaredSymbol(classSyntaxNode, cancellationToken);
        if (classSymbol is null || !DoesExtendClass(classSymbol.BaseType, typeNodeClass))
        {
            return null;
        }

        bool hasGodotImpl = !classSymbol.GetMembers(this.OverrideEventFunctionName).IsEmpty;
        List<MethodCall> primaryMethodCalls = classSymbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Method)
            .Cast<IMethodSymbol>()
            .Select(
                m => GetMethodCall(
                    classSymbol,
                    m,
                    this.AllowDisposableReturns,
                    typeIDisposable,
                    typeAutoDisposeAttribute,
                    typeAttribute))
            .Where(m => m?.ShouldCallFromPrimary == true)
            .ToList()!;

        diagnostics.AddRange(primaryMethodCalls.SelectMany(m => m.Diagnostics));

        if (!isPartial && primaryMethodCalls.Count > 0)
        {
            // Probably redundant with Godot's own diagnostics but /shrug
            diagnostics.Add(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "GH0001",
                        "Node class with GodotHat attributes is not partial",
                        " {0} class declaration should have partial modifier so source can be generated.",
                        "GodotHat.generation",
                        DiagnosticSeverity.Warning,
                        true),
                    classSymbol.Locations.FirstOrDefault(),
                    classSymbol.Name,
                    classSymbol.ContainingNamespace.ToString()));
        }

        return new ClassToProcess(
            classSyntaxNode,
            classSymbol,
            primaryMethodCalls,
            new List<string>(),
            hasGodotImpl,
            diagnostics);
    }

    protected static MethodCall? GetMethodCall(
        INamedTypeSymbol classSymbol,
        IMethodSymbol method,
        bool allowDisposableReturns,
        INamedTypeSymbol typeIDisposable,
        INamedTypeSymbol typeAutoDisposeAttribute,
        params INamedTypeSymbol[] typePrimaryAttributes)
    {
        bool returnsVoid = method.ReturnsVoid;
        bool returnsDisposable = DoesImplementInterface(method.ReturnType, typeIDisposable);
        INamedTypeSymbol? primaryAttribute = Array.Find(
            typePrimaryAttributes,
            attr => method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr)));
        bool hasAutoDisposeAttribute = method.GetAttributes()
            .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeAutoDisposeAttribute));

        MethodCallType type = 0;
        var diagnostics = new List<Diagnostic>();

        if (primaryAttribute is not null)
        {
            if (method.Arity != 0)
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GH0003",
                            $"Method with attribute [{primaryAttribute.Name}] must not take any parameters.",
                            $" {classSymbol.Name}.{method.Name} method declaration should have an empty parameter list.",
                            "GodotHat.generation",
                            DiagnosticSeverity.Warning,
                            true),
                        classSymbol.Locations.FirstOrDefault()));
            }
            else
            {
                type |= MethodCallType.PrimaryEvent;
            }

            if (allowDisposableReturns && returnsDisposable)
            {
                type |= MethodCallType.DisposeOnExitTree;
            }

            if (!returnsVoid && !(allowDisposableReturns && returnsDisposable))
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GH0004",
                            $"Method with attribute [{primaryAttribute.Name}] should return void.",
                            $" {classSymbol.Name}.{method.Name} should return void.",
                            "GodotHat.generation",
                            DiagnosticSeverity.Warning,
                            true),
                        classSymbol.Locations.FirstOrDefault()));
            }
        }

        if (hasAutoDisposeAttribute)
        {
            if (method.DeclaredAccessibility != Accessibility.Private)
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GH0005",
                            "Method with attribute [AutoDispose] should be private",
                            $" {classSymbol.Name}.{method.Name} should be private. The generated Update{method.Name} method will be public",
                            "GodotHat.generation",
                            DiagnosticSeverity.Warning,
                            true),
                        classSymbol.Locations.FirstOrDefault()));
            }

            if (returnsDisposable)
            {
                type |= MethodCallType.DisposeOnExitTree | MethodCallType.AutoDisposable;
            }
            else
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GH0006",
                            "Method with attribute [AutoDispose] should return IDisposable",
                            $" {classSymbol.Name}.{method.Name} should return IDisposable (or IDisposable?).",
                            "GodotHat.generation",
                            DiagnosticSeverity.Warning,
                            true),
                        classSymbol.Locations.FirstOrDefault()));
            }
        }

        if (type != 0 || diagnostics.Any())
        {
            return new MethodCall(method.Name, type, method, diagnostics);
        }

        return null;
    }

    private void GenerateNodeAdditions(
        SourceProductionContext context,
        ImmutableArray<ClassToProcess> nodeTypes)
    {
        if (nodeTypes.IsEmpty)
        {
            return;
        }

        foreach (ClassToProcess classToProcess in nodeTypes)
        {
            classToProcess.Diagnostics.ForEach(context.ReportDiagnostic);

            if (IsAnythingToGenerate(classToProcess))
            {
                if (classToProcess.HasTargetMethodAlready)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "GH0002",
                                "Node class with GodotHat attributes already implements Godot override",
                                $" {{0}} class declaration should not have a {this.OverrideEventFunctionName} " +
                                $"function defined, so it can be generated instead. Use [{this.AttributeShortName}] attribute " +
                                $"on an method instead, or remove the other [{this.AttributeShortName}] members.",
                                "GodotHat.generation",
                                DiagnosticSeverity.Warning,
                                true),
                            classToProcess.Symbol.Locations.FirstOrDefault(),
                            classToProcess.Symbol.Name,
                            classToProcess.Symbol.ContainingNamespace.ToString()));
                }

                this.GenerateNodeAdditions(
                    context,
                    classToProcess);
            }
        }
    }

    private static bool IsAnythingToGenerate(ClassToProcess classToProcess)
    {
        bool isPartial = classToProcess.Syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        return isPartial && (classToProcess.MethodSources.Any() || classToProcess.MethodsToCall.Any());
    }

    private void GenerateNodeAdditions(
        SourceProductionContext context,
        ClassToProcess classToProcess)
    {
        INamedTypeSymbol classSymbol = classToProcess.Symbol;
        ClassDeclarationSyntax classSyntaxNode = classToProcess.Syntax;

        string calls = string.Concat(
            classToProcess.MethodsToCall
                .Select(m => m.PrimaryCallString)
                .Where(m => m is not null)
                .Select(source => $"\n        {source};"));

        string methodSources = string.Concat(
            classToProcess.MethodSources
                .SelectMany(source => $"\n\n    {source}"));

        string code = @$"// Generated code via {this.GetType().FullName}
namespace {classSymbol.ContainingNamespace};

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

#nullable enable

{classSyntaxNode.Modifiers} class {classSymbol.Name}
{{

    public override void {this.OverrideEventFunctionName}()
    {{
        // Generated code, to add other calls add [{this.AttributeShortName}] attributes to methods
{calls}
    }}{methodSources}
}}
";

        context.AddSource(
            $"{classSymbol.ContainingNamespace}.{classSymbol.Name}_{this.AttributeShortName}.generated.cs",
            code);
    }

    private static IEnumerable<ITypeSymbol> GetThisAndBaseTypes(ITypeSymbol? symbol)
    {
        if (symbol is null)
        {
            yield break;
        }

        ITypeSymbol? current = symbol;
        do
        {
            yield return current;
            current = current.BaseType;
        } while (current is not null);
    }

    protected static bool DoesExtendClass(ITypeSymbol? symbol, ISymbol typeNodeClass)
    {
        return GetThisAndBaseTypes(symbol).Any(t => SymbolEqualityComparer.Default.Equals(t, typeNodeClass));
    }

    protected static bool DoesImplementInterface(ITypeSymbol? symbol, ISymbol typeNodeInterface)
    {
        if (symbol is null)
        {
            return false;
        }
        return SymbolEqualityComparer.Default.Equals(symbol, typeNodeInterface) ||
               symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, typeNodeInterface));
    }

    protected static INamedTypeSymbol GetRequiredType(SemanticModel model, string typeName)
    {
        INamedTypeSymbol? typeSymbol = model.Compilation.GetTypeByMetadataName(typeName);
        if (typeSymbol is null)
        {
            throw new InvalidOperationException($"Failed to resolve {typeName}, is it in a referenced assembly?");
        }
        return typeSymbol;
    }

    [Flags]
    protected enum MethodCallType
    {
        PrimaryEvent = (1 << 0),
        DisposeOnExitTree = (1 << 1),
        AutoDisposable = (2 << 1),
    }

    protected record class MethodCall(
        string Name,
        MethodCallType Type,
        IMethodSymbol? Symbol = null,
        List<Diagnostic>? Diagnostics = null)
    {
        public string Name { get; } = Name;
        public MethodCallType Type { get; } = Type;
        public IMethodSymbol? Symbol { get; } = Symbol;
        public List<Diagnostic>? Diagnostics { get; } = Diagnostics;

        public bool ShouldCallFromPrimary => (this.Type & MethodCallType.PrimaryEvent) != 0;
        public bool ShouldCallDisposable => (this.Type & MethodCallType.DisposeOnExitTree) != 0;
        public bool IsAutoDisposable => (this.Type & MethodCallType.AutoDisposable) != 0;
        public string? DisposableMemberName => this.ShouldCallDisposable ? $"__disposable_{this.Name}" : null;
        public string? DisposableMethodName => this.IsAutoDisposable
            ? $"Dispose{this.Name}"
            : this.ShouldCallDisposable
                ? $"__Dispose_{this.Name}"
                : null;

        public string? PrimaryCallString
        {
            get
            {
                if (!this.ShouldCallFromPrimary)
                {
                    return null;
                }

                if (this.ShouldCallDisposable)
                {
                    if (this.IsAutoDisposable)
                    {
                        return $"Update{this.Name}()";
                    }
                    return $"{this.DisposableMemberName} = {this.Name}()";
                }

                return $"{this.Name}()";
            }
        }

        public string? DisposeCallString
        {
            get
            {
                if (!this.ShouldCallDisposable)
                {
                    return null;
                }

                if (this.IsAutoDisposable)
                {
                    return $"Dispose{this.Name}()";
                }

                return $"__Dispose_{this.Name}()";
            }
        }
    }

    protected record ClassToProcess(
        ClassDeclarationSyntax Syntax,
        INamedTypeSymbol Symbol,
        List<MethodCall> MethodsToCall,
        List<string> MethodSources,
        bool HasTargetMethodAlready,
        List<Diagnostic> Diagnostics)
    {
        public ClassDeclarationSyntax Syntax { get; } = Syntax;

        public INamedTypeSymbol Symbol { get; } = Symbol;

        public List<MethodCall> MethodsToCall { get; } = MethodsToCall;

        public List<Diagnostic> Diagnostics { get; } = Diagnostics;

        public List<string> MethodSources { get; } = MethodSources;

        public bool HasTargetMethodAlready { get; } = HasTargetMethodAlready;
    }
}

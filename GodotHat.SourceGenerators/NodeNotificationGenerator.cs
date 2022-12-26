using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotHat.SourceGenerators;

public abstract class NodeNotificationGenerator : IIncrementalGenerator
{
    protected abstract string AttributeFullName { get; }
    protected abstract string AttributeShortName { get; }
    protected abstract string OverrideEventFunctionName { get; }
    protected abstract bool AllowDisposableReturns { get; }

    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var nodeTypes = context.SyntaxProvider.CreateSyntaxProvider(
                (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                this.GetNode)
            .Where(type => type is not null)
            .Collect();

        context.RegisterSourceOutput(nodeTypes, GenerateNodeAdditions);
    }

    protected virtual ClassToProcess? GetNode(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        INamedTypeSymbol? typeNodeClass =
            context.SemanticModel.Compilation.GetTypeByMetadataName("Godot.Node");

        INamedTypeSymbol? typeAttribute =
            context.SemanticModel.Compilation.GetTypeByMetadataName(this.AttributeFullName);

        INamedTypeSymbol? typeIDisposable =
            context.SemanticModel.Compilation.GetTypeByMetadataName("System.IDisposable");

        if (typeNodeClass == null)
        {
            throw new InvalidOperationException("Failed to resolve Godot.Node, is it in a referenced assembly?");
        }

        if (typeAttribute == null)
        {
            throw new InvalidOperationException(
                $"Failed to resolve {this.AttributeFullName}, is it in a referenced assembly?");
        }

        if (typeIDisposable is null)
        {
            throw new InvalidOperationException("System.IDisposable not found");
        }

        var classSyntaxNode = (ClassDeclarationSyntax)context.Node;
        bool isPartial = classSyntaxNode.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        List<Diagnostic> diagnostics = new List<Diagnostic>();

        INamedTypeSymbol? classSymbol = context.SemanticModel.GetDeclaredSymbol(classSyntaxNode, cancellationToken);
        if (classSymbol is null || !DoesExtendClass(classSymbol.BaseType, typeNodeClass))
        {
            return null;
        }

        // TODO: emit diagnostic if attributes are on anything that is filtered out, ie wrong return type, wrong args etc

        bool hasGodotImpl = !classSymbol.GetMembers(this.OverrideEventFunctionName).IsEmpty;
        var methodsWithAttribute = classSymbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Method)
            .Cast<IMethodSymbol>()
            .Where(
                m => m.Arity == 0 &&
                     (m.ReturnsVoid || DoesImplementInterface(m.ReturnType, typeIDisposable)) &&
                     m.GetAttributes()
                         .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeAttribute)))
            .Select(m => new MethodCall(m.Name, this.AllowDisposableReturns && DoesImplementInterface(m.ReturnType, typeIDisposable)))
            .ToList();

        var methodSources = new List<string>();
        foreach (var call in methodsWithAttribute.Where(m => m.IsDisposableReturn))
        {
            methodSources.Add($"private IDisposable? __disposable_{call.Name};");
            methodSources.Add(@$"private void __Dispose_{call.Name}()
    {{
        __disposable_{call.Name}?.Dispose();
        __disposable_{call.Name} = null;
    }}");
        }

        if (!isPartial && methodsWithAttribute.Count > 0)
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
            methodsWithAttribute,
            methodSources,
            hasGodotImpl,
            diagnostics);
    }

    private void GenerateNodeAdditions(
        SourceProductionContext context,
        ImmutableArray<ClassToProcess?> nodeTypes)
    {
        if (nodeTypes.IsEmpty)
        {
            return;
        }

        foreach (ClassToProcess? classToProcess in nodeTypes)
        {
            if (classToProcess is null)
            {
                throw new InvalidOperationException("Unexpected null");
            }

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

                GenerateNodeAdditions(
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

    protected record class MethodCall(
        string Name,
        bool IsDisposableReturn)
    {
        public string Name { get; } = Name;
        public bool IsDisposableReturn { get; } = IsDisposableReturn;

        public override string ToString()
        {
            if (this.IsDisposableReturn)
            {
                return $"__disposable_{Name} = {Name}()";
            }
            return $"{Name}()";
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

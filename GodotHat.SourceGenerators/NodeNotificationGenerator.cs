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

        if (typeNodeClass == null)
        {
            throw new InvalidOperationException("Failed to resolve Godot.Node, is it in a referenced assembly?");
        }

        if (typeAttribute == null)
        {
            throw new InvalidOperationException(
                $"Failed to resolve {this.AttributeFullName}, is it in a referenced assembly?");
        }

        var classSyntaxNode = (ClassDeclarationSyntax)context.Node;
        bool isPartial = classSyntaxNode.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        List<Diagnostic> diagnostics = new List<Diagnostic>();

        INamedTypeSymbol? classSymbol = context.SemanticModel.GetDeclaredSymbol(classSyntaxNode, cancellationToken);
        if (classSymbol is null || !DoesExtendNode(classSymbol.BaseType, typeNodeClass))
        {
            return null;
        }

        bool hasGodotImpl = !classSymbol.GetMembers(this.OverrideEventFunctionName).IsEmpty;
        var methodsWithAttribute = classSymbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Method)
            .Cast<IMethodSymbol>()
            .Where(
                m => m.Arity == 0 &&
                     m.ReturnsVoid &&
                     m.GetAttributes()
                         .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeAttribute)))
            .Select(m => m.Name)
            .ToList();

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

        if (hasGodotImpl)
        {
            diagnostics.Add(
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
                    classSymbol.Locations.FirstOrDefault(),
                    classSymbol.Name,
                    classSymbol.ContainingNamespace.ToString()));
        }

        return methodsWithAttribute.Count == 0
            ? null
            : new ClassToProcess(classSyntaxNode, classSymbol, methodsWithAttribute, diagnostics);
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

            // Only generate if partial. If not, there will be a diagnostic
            if (classToProcess.Syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                GenerateNodeAdditions(
                    context,
                    classToProcess);
            }
        }
    }

    private void GenerateNodeAdditions(
        SourceProductionContext context,
        ClassToProcess classToProcess)
    {
        INamedTypeSymbol classSymbol = classToProcess.Symbol;
        ClassDeclarationSyntax classSyntaxNode = classToProcess.Syntax;

        string calls = string.Concat(
            classToProcess.SubMethodsToCall
                .Select(source => $"        {source}();{Environment.NewLine}"));

        string methodSources = string.Concat(
            classToProcess.MethodSources
                .SelectMany(source => $"    {source}{Environment.NewLine}{Environment.NewLine}"));

        string code = @$"// Generated code via {this.GetType().FullName}
namespace {classSymbol.ContainingNamespace};

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

{classSyntaxNode.Modifiers} class {classSymbol.Name} {{

    public override void {this.OverrideEventFunctionName}() {{
        // Generated code, to add other calls add [{this.AttributeShortName}] attributes to methods
{calls}
    }}

{methodSources}
}}
";

        context.AddSource(
            $"{classSymbol.ContainingNamespace}.{classSymbol.Name}_{this.AttributeShortName}.generated.cs",
            code);
    }

    private static IEnumerable<INamedTypeSymbol> GetThisAndBaseTypes(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            yield break;
        }

        INamedTypeSymbol? current = symbol;
        do
        {
            yield return current;
            current = current.BaseType;
        } while (current is not null);
    }

    private static bool DoesExtendNode(INamedTypeSymbol? symbol, ISymbol typeNodeClass)
    {
        return GetThisAndBaseTypes(symbol).Any(t => SymbolEqualityComparer.Default.Equals(t, typeNodeClass));
    }

    protected record class ClassToProcess(
        ClassDeclarationSyntax Syntax,
        INamedTypeSymbol Symbol,
        List<string> SubMethodsToCall,
        List<Diagnostic> Diagnostics)
    {
        public ClassDeclarationSyntax Syntax { get; } = Syntax;

        public INamedTypeSymbol Symbol { get; } = Symbol;

        public List<string> SubMethodsToCall { get; } = SubMethodsToCall;

        public List<Diagnostic> Diagnostics { get; } = Diagnostics;

        public virtual IEnumerable<string> MethodSources => ImmutableList<string>.Empty;
    }
}

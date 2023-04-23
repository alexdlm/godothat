using System.Collections.Immutable;
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
        INamedTypeSymbol typeNodeClass = GeneratorUtil.GetRequiredType(context.SemanticModel, "Godot.Node");
        INamedTypeSymbol typeAttribute = GeneratorUtil.GetRequiredType(context.SemanticModel, this.AttributeFullName);
        INamedTypeSymbol typeToolAttribute =
            GeneratorUtil.GetRequiredType(context.SemanticModel, "Godot.ToolAttribute");
        INamedTypeSymbol typeIDisposable = GeneratorUtil.GetRequiredType(context.SemanticModel, "System.IDisposable");
        INamedTypeSymbol typeAutoDisposeAttribute = GeneratorUtil.GetRequiredType(
            context.SemanticModel,
            "GodotHat.AutoDisposeAttribute");

        var classSyntaxNode = (ClassDeclarationSyntax)context.Node;
        bool isPartial = classSyntaxNode.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        var diagnostics = new List<Diagnostic>();

        INamedTypeSymbol? classSymbol = context.SemanticModel.GetDeclaredSymbol(classSyntaxNode, cancellationToken);
        if (classSymbol is null || !GeneratorUtil.DoesExtendClass(classSymbol.BaseType, typeNodeClass))
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
            diagnostics.Add(Diagnostics.CreateNodeNotPartial(classSymbol));
        }

        bool isTool = classSymbol.GetAttributes()
            .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, typeToolAttribute));

        return new ClassToProcess(
            classSyntaxNode,
            classSymbol,
            isTool,
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
        bool returnsDisposable = GeneratorUtil.DoesImplementInterface(method.ReturnType, typeIDisposable);
        INamedTypeSymbol? primaryAttribute = Array.Find(
            typePrimaryAttributes,
            attr => method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr)));
        bool hasAutoDisposeAttribute = method.GetAttributes()
            .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeAutoDisposeAttribute));

        MethodCallType type = 0;
        var diagnostics = new List<Diagnostic>();

        if (primaryAttribute is not null)
        {
            if (method.Parameters.Length != 0)
            {
                diagnostics.Add(Diagnostics.CreateMethodShouldHaveNoParams(classSymbol, primaryAttribute, method));
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
                diagnostics.Add(Diagnostics.CreateMethodShouldReturnVoid(classSymbol, primaryAttribute, method));
            }
        }

        if (hasAutoDisposeAttribute)
        {
            if (method.DeclaredAccessibility != Accessibility.Private)
            {
                diagnostics.Add(Diagnostics.CreateMethodShouldBePrivate(classSymbol, typeAutoDisposeAttribute, method));
            }

            if (returnsDisposable)
            {
                type |= MethodCallType.DisposeOnExitTree | MethodCallType.AutoDisposable;
            }
            else
            {
                diagnostics.Add(
                    Diagnostics.CreateMethodShouldReturnIDisposable(classSymbol, typeAutoDisposeAttribute, method));
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
                        Diagnostics.CreateNodeAlreadyContainsMethod(
                            classToProcess.Symbol,
                            this.OverrideEventFunctionName,
                            this.AttributeShortName));
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

        string functionImpl;

        if (classToProcess.IsTool)
        {
            functionImpl = $@"    public override void {this.OverrideEventFunctionName}()
    {{
        base.{this.OverrideEventFunctionName}();
#if TOOLS
        if (Godot.Engine.IsEditorHint())
        {{
            try
            {{
                _{this.OverrideEventFunctionName}Internal();
            }}
            catch (Exception e)
            {{
                GD.PrintErr($""Caught exception in {{this.GetPath()}}.{this.OverrideEventFunctionName}()"", e);
            }}
        }}
        else
        {{
#endif //TOOLS
            _{this.OverrideEventFunctionName}Internal();
#if TOOLS
        }}
#endif //TOOLS
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void _{this.OverrideEventFunctionName}Internal()
    {{
        // Generated code, to add other calls add [{this.AttributeShortName}] attributes to methods
{calls}
    }}";
        }
        else
        {
            functionImpl = $@"    public override void {this.OverrideEventFunctionName}()
    {{
        // Generated code, to add other calls add [{this.AttributeShortName}] attributes to methods
{calls}
    }}";
        }

        string code = @$"// Generated code via {this.GetType().FullName}
namespace {classSymbol.ContainingNamespace};

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Godot;

#nullable enable

{classSyntaxNode.Modifiers} class {classSymbol.Name}
{{
{functionImpl}{methodSources}
}}
";

        context.AddSource(
            $"{classSymbol.ContainingNamespace}.{classSymbol.Name}_{this.AttributeShortName}.generated.cs",
            code);
    }

    [Flags]
    protected enum MethodCallType
    {
        PrimaryEvent = 1 << 0,
        DisposeOnExitTree = 1 << 1,
        AutoDisposable = 2 << 1,
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
        bool IsTool,
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
        public bool IsTool { get; } = IsTool;
    }
}

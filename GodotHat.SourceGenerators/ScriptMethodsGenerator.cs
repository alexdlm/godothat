using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotHat.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class ScriptMethodsGenerator : IIncrementalGenerator
{
    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<ClassToProcess>> nodeTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                GetNode)
            .Where(n => n is not null)
            .Collect()!;

        context.RegisterSourceOutput(nodeTypes, this.GenerateNodeAdditions);
    }

    private static ClassToProcess? GetNode(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        INamedTypeSymbol typeNodeClass = GeneratorUtil.GetRequiredType(context.SemanticModel, "Godot.Node");
        INamedTypeSymbol typeAutoDisposeAttribute =
            GeneratorUtil.GetRequiredType(context.SemanticModel, "GodotHat.AutoDisposeAttribute");
        INamedTypeSymbol typeOnEnterTreeAttribute =
            GeneratorUtil.GetRequiredType(context.SemanticModel, "GodotHat.OnEnterTreeAttribute");
        INamedTypeSymbol typeOnExitTreeAttribute =
            GeneratorUtil.GetRequiredType(context.SemanticModel, "GodotHat.OnExitTreeAttribute");
        INamedTypeSymbol typeOnReadyAttribute =
            GeneratorUtil.GetRequiredType(context.SemanticModel, "GodotHat.OnReadyAttribute");
        INamedTypeSymbol typeSceneUniqueNameAttribute =
            GeneratorUtil.GetRequiredType(context.SemanticModel, "GodotHat.SceneUniqueNameAttribute");
        INamedTypeSymbol typeIDisposable =
            GeneratorUtil.GetRequiredType(context.SemanticModel, "System.IDisposable");

        var classSyntaxNode = (ClassDeclarationSyntax)context.Node;
        bool isPartial = classSyntaxNode.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        var diagnostics = new List<Diagnostic>();

        INamedTypeSymbol? classSymbol = context.SemanticModel.GetDeclaredSymbol(classSyntaxNode, cancellationToken);
        if (classSymbol is null || !GeneratorUtil.DoesExtendClass(classSymbol.BaseType, typeNodeClass))
        {
            return null;
        }

        // For the moment, we don't care about exposing ad-hoc C# methods, we probably will need to reproduce the godot
        // marshalling in the future though.
        // Basically we want to generate enough for the standard methods to be callable, whether defined by our generators
        // or manually.

        // TODO: provide diagnostic for methods that do not fit this model! OTOH putting enough work in to identify
        //       these is probably close to just implementing anyway.

        var members = classSymbol.GetMembers();

        var methods = members
            .Where(s => s is { Kind: SymbolKind.Method, IsImplicitlyDeclared: false, IsStatic: false })
            .Cast<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && m.RefKind == RefKind.None)
            .Select(
                m =>
                {
                    GodotSourceGeneratorsUtil.GodotType? returnType =
                        GodotSourceGeneratorsUtil.GetGodotType(m.ReturnType);
                    if (!m.ReturnsVoid && returnType is null)
                    {
                        return null;
                    }

                    // TODO: convert params
                    List<GodotSourceGeneratorsUtil.GodotType?> arguments = m.Parameters
                        .Select(p => p.Type)
                        .Select(GodotSourceGeneratorsUtil.GetGodotType)
                        .ToList();

                    if (arguments.Count > 0 && arguments.Any(t => t is null))
                    {
                        return null;
                    }

                    return new GodotSourceGeneratorsUtil.GodotMethod(m.Name, returnType, m, arguments!);
                })
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();

        bool addOnEnterTree = HasAttributesOnAnyMethod(classSymbol.GetMembers(), typeOnEnterTreeAttribute).Any() ||
                              HasAttributesOnAnyField(classSymbol.GetMembers(), typeSceneUniqueNameAttribute).Any() ||
                              HasAttributesOnAnyProperty(classSymbol.GetMembers(), typeSceneUniqueNameAttribute).Any();
        bool addOnExitTree = HasAttributesOnAnyMethod(classSymbol.GetMembers(), typeOnExitTreeAttribute).Any() ||
                             HasAttributesOnAnyMethod(classSymbol.GetMembers(), typeAutoDisposeAttribute)
                                 .Any(m => GeneratorUtil.DoesImplementInterface(m.ReturnType, typeIDisposable)) ||
                             HasAttributesOnAnyMethod(classSymbol.GetMembers(), typeOnReadyAttribute)
                                 .Any(m => GeneratorUtil.DoesImplementInterface(m.ReturnType, typeIDisposable)) ||
                             HasAttributesOnAnyMethod(classSymbol.GetMembers(), typeAutoDisposeAttribute)
                                 .Any(m => GeneratorUtil.DoesImplementInterface(m.ReturnType, typeIDisposable));
        bool addOnReady = HasAttributesOnAnyMethod(classSymbol.GetMembers(), typeOnReadyAttribute).Any();

        if (addOnEnterTree)
        {
            methods.Add(
                new GodotSourceGeneratorsUtil.GodotMethod(
                    "_EnterTree",
                    GodotSourceGeneratorsUtil.GodotType.Void));
        }

        if (addOnExitTree)
        {
            methods.Add(
                new GodotSourceGeneratorsUtil.GodotMethod(
                    "_ExitTree",
                    GodotSourceGeneratorsUtil.GodotType.Void));
        }

        if (addOnReady)
        {
            methods.Add(
                new GodotSourceGeneratorsUtil.GodotMethod(
                    "_Ready",
                    GodotSourceGeneratorsUtil.GodotType.Void));
        }

        return new ClassToProcess(
            classSyntaxNode,
            classSymbol,
            methods,
            diagnostics);
    }

    private static IEnumerable<IFieldSymbol> HasAttributesOnAnyField(
        IEnumerable<ISymbol> members,
        params INamedTypeSymbol[] attributeTypes)
    {
        return members.Where(m => m.Kind == SymbolKind.Field)
            .Cast<IFieldSymbol>()
            // Do any fields have all attributes
            .Where(
                m => attributeTypes.All(
                    attr => m.GetAttributes()
                        .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr))));
    }

    private static IEnumerable<IPropertySymbol> HasAttributesOnAnyProperty(
        IEnumerable<ISymbol> members,
        params INamedTypeSymbol[] attributeTypes)
    {
        return members.Where(m => m.Kind == SymbolKind.Property)
            .Cast<IPropertySymbol>()
            // Do any properties have all attributes
            .Where(
                m => attributeTypes.All(
                    attr => m.GetAttributes()
                        .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr))));
    }

    private static IEnumerable<IMethodSymbol> HasAttributesOnAnyMethod(
        IEnumerable<ISymbol> members,
        params INamedTypeSymbol[] attributeTypes)
    {
        return members.Where(m => m.Kind == SymbolKind.Method)
            .Cast<IMethodSymbol>()
            // Do any methods have all attributes
            .Where(
                m => attributeTypes.All(
                    attr => m.GetAttributes()
                        .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attr))));
    }

    private void GenerateNodeAdditions(SourceProductionContext context, ImmutableArray<ClassToProcess> nodeTypes)
    {
        if (nodeTypes.IsEmpty)
        {
            return;
        }

        foreach (ClassToProcess classToProcess in nodeTypes)
        {
            classToProcess.Diagnostics.ForEach(context.ReportDiagnostic);

            if (classToProcess.Methods.Count > 0)
            {
                this.GenerateNodeAdditions(
                    context,
                    classToProcess);
            }
        }
    }

    private static string GodotMethodToBridgeMethodInfo(GodotSourceGeneratorsUtil.GodotMethod method, string idx)
    {
        return @$"    private static readonly global::Godot.Bridge.MethodInfo MethodInfo_{method.Name}{idx} = new(
        name: MethodName.{method.Name},
        returnVal: new(
            type: global::Godot.Variant.Type.{method.ReturnType?.VariantType ?? GodotVariantType.Nil},
            name: new global::Godot.StringName(),
            hint: global::Godot.PropertyHint.None,
            hintString: """",
            usage: global::Godot.PropertyUsageFlags.Storage | global::Godot.PropertyUsageFlags.Editor,
            exported: false),
        flags: global::Godot.MethodFlags.Normal,
        arguments: null,
        defaultArguments: null);

";
    }

    private static string GodotMethodToInvokeCondition(GodotSourceGeneratorsUtil.GodotMethod method, string _idx)
    {
        string args = method.Arguments is null
            ? ""
            : string.Join(
                ",",
                method.Arguments.Select(
                    (p, index) =>
                        $@"
                global::Godot.NativeInterop.VariantUtils.ConvertTo<{p.QualifiedName}>(args[{index}])"));

        return $@"        if (method == MethodName.{method.Name} && args.Count == {method.Arguments?.Count ?? 0})
        {{
            {method.Name}({args});
            ret = default;
            return true;
        }}
";
    }

    private void GenerateNodeAdditions(SourceProductionContext context, ClassToProcess classToProcess)
    {
        INamedTypeSymbol classSymbol = classToProcess.Symbol;
        ClassDeclarationSyntax classSyntaxNode = classToProcess.Syntax;

        INamedTypeSymbol? godotBaseClass = GodotSourceGeneratorsUtil.GetGodotParentClass(classSymbol);
        if (godotBaseClass == null)
        {
            throw new Exception(
                $"Processing a class ({classSymbol.Name}) that does not derive from a Godot base class! ");
        }
        if (godotBaseClass.GetTypeMembers("MethodName").IsEmpty)
        {
            throw new Exception(
                $"Parent class ({godotBaseClass.Name}) of {classSymbol.Name} does not contain MethodName inner class!");
        }

        var lf = SyntaxFactory.ElasticCarriageReturnLineFeed;

        List<string> methodNames = classToProcess.Methods.Select(m => m.Name).Distinct().OrderBy(m => m).ToList();
        string methodNameMembers = string.Concat(
            methodNames
                .Select(m => $"        public new static readonly global::Godot.StringName {m} = \"{m}\";{lf}"));

        var orderedMethods = classToProcess.Methods
            .GroupBy(item => item.Name)
            .SelectMany(
                group => group.Select(
                    (method, index) => (method, idx: index > 0 ? $"{index + 1}" : ""))) // name, idx if idx > 0
            .OrderBy(m => m.method.Name)
            .ThenBy(m => m.idx)
            .ToList();

        string methodNameConstants =
            string.Concat(orderedMethods.Select(m => GodotMethodToBridgeMethodInfo(m.method, m.idx)));

        string methodNameMethodsListAdds = string.Concat(
            orderedMethods
                .Select(m => $"        methods.Add(MethodInfo_{m.method.Name}{m.idx});{lf}"));

        string methodInvokes = String.Concat(
            orderedMethods
                .Select(m => GodotMethodToInvokeCondition(m.method, m.idx)));

        string methodHasCases = String.Concat(
            orderedMethods.Select(m => $"        if (method == MethodName.{m.method.Name}) return true;{lf}"));

        string code = @$"// Generated code via {this.GetType().FullName}
namespace {classSymbol.ContainingNamespace};

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot.NativeInterop;
using Godot;

#nullable enable

{classSyntaxNode.Modifiers} class {classSymbol.Name}
{{
    #pragma warning disable CS0109 // Disable warning about redundant 'new' keyword
    public new class MethodName : global::{godotBaseClass.ToDisplayString(NullableFlowState.None)}.MethodName
    {{
{methodNameMembers}
    }}

{methodNameConstants}
    internal new static global::System.Collections.Generic.List<global::Godot.Bridge.MethodInfo> GetGodotMethodList()
    {{
        var methods = new global::System.Collections.Generic.List<global::Godot.Bridge.MethodInfo>({methodNames.Count});
{methodNameMethodsListAdds}
        return methods;
    }}

    #pragma warning restore CS0109

    protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
    {{
{methodInvokes}
        return base.InvokeGodotClassMethod(method, args, out ret);
    }}

    protected override bool HasGodotClassMethod(in godot_string_name method)
    {{
{methodHasCases}
        return base.HasGodotClassMethod(method);
    }}
}}
";

        context.AddSource(
            $"{classSymbol.ContainingNamespace}.{classSymbol.Name}_ScriptMethods.generated.cs",
            code);
    }


    private record ClassToProcess(
        ClassDeclarationSyntax Syntax,
        INamedTypeSymbol Symbol,
        List<GodotSourceGeneratorsUtil.GodotMethod> Methods,
        List<Diagnostic> Diagnostics)
    {
        public ClassDeclarationSyntax Syntax { get; } = Syntax;

        public INamedTypeSymbol Symbol { get; } = Symbol;

        public List<GodotSourceGeneratorsUtil.GodotMethod> Methods { get; } = Methods;

        public List<Diagnostic> Diagnostics { get; } = Diagnostics;
    }
}

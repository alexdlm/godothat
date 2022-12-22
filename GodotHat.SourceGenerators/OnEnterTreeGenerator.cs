using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotHat.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class OnEnterTreeGenerator : NodeNotificationGenerator
{
    protected override string AttributeFullName => "GodotHat.OnEnterTreeAttribute";
    protected override string AttributeShortName => "OnEnterTree";
    protected override string OverrideEventFunctionName => "_EnterTree";

    protected override ClassToProcess? GetNode(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        ClassToProcess? classToProcess = base.GetNode(context, cancellationToken);
        if (classToProcess is null)
        {
            return null;
        }

        INamedTypeSymbol? typeSceneUniqueNameAttribute =
            context.SemanticModel.Compilation.GetTypeByMetadataName("GodotHat.SceneUniqueNameAttribute");

        var fieldsWithAttribute = classToProcess.Symbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Field)
            .Cast<IFieldSymbol>()
            .Select(
                f => (Field: f, Attr: f.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeSceneUniqueNameAttribute))))
            .Where(tup => (tup.Attr) is not null)
            .ToList();

        var propsWithAttribute = classToProcess.Symbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Property)
            .Cast<IPropertySymbol>()
            .Select(
                f => (Prop: f, Attr: f.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeSceneUniqueNameAttribute))))
            .Where(tup => (tup.Attr) is not null)
            .ToList();

        List<string> methodSources = new List<string>();
        List<string> methodsToCall = new List<string>();

        foreach (var (fieldSymbol, attr) in fieldsWithAttribute)
        {
            var nullable = fieldSymbol.NullableAnnotation == NullableAnnotation.Annotated;
            var uniqueName = attr!.ConstructorArguments[0].ToCSharpString();
            var required = attr.ConstructorArguments.Length == 1 ? null : attr.ConstructorArguments[1].Value as Boolean?;

            var isActuallyNullable = nullable || (required is not null && required == false);
            var getNodeFunc = isActuallyNullable ? "GetNodeOrNull" : "GetNode";

            methodSources.Add(@$"private void __InitFromScene_{fieldSymbol.Name}() {{
        this.{fieldSymbol.Name} = this.{getNodeFunc}<{fieldSymbol.Type.ToDisplayString(NullableFlowState.None)}>({uniqueName});
    }}");
            methodsToCall.Add($"__InitFromScene_{fieldSymbol.Name}");
        }

        foreach (var (propSymbol, attr) in propsWithAttribute)
        {
            var nullable = propSymbol.NullableAnnotation == NullableAnnotation.Annotated;
            var uniqueName = attr!.ConstructorArguments[0].ToCSharpString();
            var required = attr.ConstructorArguments.Length == 1 ? null : attr.ConstructorArguments[1].Value as Boolean?;

            var isActuallyNullable = nullable || (required is not null && required == false);
            var getNodeFunc = isActuallyNullable ? "GetNodeOrNull" : "GetNode";

            methodSources.Add(@$"private void __InitFromScene_{propSymbol.Name}() {{
        this.{propSymbol.Name} = this.{getNodeFunc}<{propSymbol.Type.ToDisplayString(NullableFlowState.None)}>({uniqueName});
    }}");
            methodsToCall.Add($"__InitFromScene_{propSymbol.Name}");
        }

        classToProcess.SubMethodsToCall.ForEach(methodsToCall.Add);

        // Also the new methods we generate
        return new WithExtraMethods(
            classToProcess.Syntax,
            classToProcess.Symbol,
            methodsToCall,
            methodSources,
            classToProcess.Diagnostics);
    }

    protected record class WithExtraMethods(
        ClassDeclarationSyntax Syntax,
        INamedTypeSymbol Symbol,
        List<string> SubMethodsToCall,
        IEnumerable<string> MethodSources,
        List<Diagnostic> Diagnostics) : ClassToProcess(Syntax, Symbol, SubMethodsToCall, Diagnostics)
    {
        public override IEnumerable<string> MethodSources { get; } = MethodSources;
    }
}

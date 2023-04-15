using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GodotHat.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class OnEnterTreeGenerator : AbstractNodeNotificationGenerator
{
    protected override string AttributeFullName => "GodotHat.OnEnterTreeAttribute";
    protected override string AttributeShortName => "OnEnterTree";
    protected override string OverrideEventFunctionName => "_EnterTree";
    protected override bool AllowDisposableReturns => true;

    protected override ClassToProcess? GetNode(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        ClassToProcess? classToProcess = base.GetNode(context, cancellationToken);
        return classToProcess is not null ? GetWithSceneUniqueNameInitializers(context, classToProcess) : null;
    }

    private static ClassToProcess GetWithSceneUniqueNameInitializers(
        GeneratorSyntaxContext context,
        ClassToProcess classToProcess)
    {
        INamedTypeSymbol typeSceneUniqueNameAttribute = GeneratorUtil.GetRequiredType(
            context.SemanticModel,
            "GodotHat.SceneUniqueNameAttribute");

        var fieldsWithAttribute = classToProcess.Symbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Field)
            .Cast<IFieldSymbol>()
            .Select(
                f => (Field: f, Attr: f.GetAttributes()
                    .FirstOrDefault(
                        a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeSceneUniqueNameAttribute))))
            .Where(tup => tup.Attr is not null)
            .ToList();

        var propsWithAttribute = classToProcess.Symbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Property)
            .Cast<IPropertySymbol>()
            .Select(
                f => (Prop: f, Attr: f.GetAttributes()
                    .FirstOrDefault(
                        a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeSceneUniqueNameAttribute))))
            .Where(tup => tup.Attr is not null)
            .ToList();

        // Nothing to do
        if (!fieldsWithAttribute.Any() && !propsWithAttribute.Any())
        {
            return classToProcess;
        }

        var methodSources = new List<string>(classToProcess.MethodSources);
        var methodsToCall = new List<MethodCall>();

        foreach ((IFieldSymbol? fieldSymbol, AttributeData? attr) in fieldsWithAttribute)
        {
            bool nullable = fieldSymbol.NullableAnnotation == NullableAnnotation.Annotated;
            // TODO: This should support named arguments too
            string uniqueName = attr!.ConstructorArguments[0].ToCSharpString();
            bool required = attr.ConstructorArguments.Length == 1 || (attr.ConstructorArguments[1].Value as bool? ?? true);

            bool isActuallyNullable = nullable || !required;
            string getNodeFunc = isActuallyNullable ? "GetNodeOrNull" : "GetNode";

            methodSources.Add(
                @$"private void __InitFromScene_{fieldSymbol.Name}()
    {{
        this.{fieldSymbol.Name} = this.{getNodeFunc}<{fieldSymbol.Type.ToDisplayString(NullableFlowState.None)}>({uniqueName});
    }}");
            methodsToCall.Add(new MethodCall($"__InitFromScene_{fieldSymbol.Name}", MethodCallType.PrimaryEvent));
        }

        foreach ((IPropertySymbol? propSymbol, AttributeData? attr) in propsWithAttribute)
        {
            bool nullable = propSymbol.NullableAnnotation == NullableAnnotation.Annotated;
            string uniqueName = attr!.ConstructorArguments[0].ToCSharpString();
            bool required = attr.ConstructorArguments.Length == 1 || (attr.ConstructorArguments[1].Value as bool? ?? true);

            bool isActuallyNullable = nullable || !required;
            string getNodeFunc = isActuallyNullable ? "GetNodeOrNull" : "GetNode";

            methodSources.Add(
                @$"private void __InitFromScene_{propSymbol.Name}()
    {{
        this.{propSymbol.Name} = this.{getNodeFunc}<{propSymbol.Type.ToDisplayString(NullableFlowState.None)}>({uniqueName});
    }}");
            methodsToCall.Add(new MethodCall($"__InitFromScene_{propSymbol.Name}", MethodCallType.PrimaryEvent));
        }

        classToProcess.MethodsToCall.ForEach(methodsToCall.Add);

        // Also the new methods we generate
        return new ClassToProcess(
            classToProcess.Syntax,
            classToProcess.Symbol,
            classToProcess.IsTool,
            methodsToCall,
            methodSources,
            classToProcess.HasTargetMethodAlready,
            classToProcess.Diagnostics);
    }
}

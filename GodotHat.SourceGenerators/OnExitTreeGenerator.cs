using System.Collections;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotHat.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public partial class OnExitTreeGenerator : NodeNotificationGenerator
{
    protected override string AttributeFullName => "GodotHat.OnExitTreeAttribute";
    protected override string AttributeShortName => "OnExitTree";
    protected override string OverrideEventFunctionName => "_ExitTree";

    protected override bool AllowDisposableReturns => false;

    protected override ClassToProcess? GetNode(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        ClassToProcess? classToProcess = base.GetNode(context, cancellationToken);
        return classToProcess is null ? null : GetWithSceneUniqueNameInitializers(context, classToProcess);
    }

    private static ClassToProcess? GetWithSceneUniqueNameInitializers(
        GeneratorSyntaxContext context,
        ClassToProcess classToProcess)
    {
        INamedTypeSymbol? typeOnEnterTreeAttribute =
            context.SemanticModel.Compilation.GetTypeByMetadataName("GodotHat.OnEnterTreeAttribute");
        INamedTypeSymbol? typeOnReadyAttribute =
            context.SemanticModel.Compilation.GetTypeByMetadataName("GodotHat.OnReadyAttribute");
        INamedTypeSymbol? typeIDisposable =
            context.SemanticModel.Compilation.GetTypeByMetadataName("System.IDisposable");

        if (typeOnEnterTreeAttribute is null || typeOnReadyAttribute is null)
        {
            throw new InvalidOperationException(
                "Failed to resolve GodotHat attributes, is the GodotHat.Attributes assembly referenced?");
        }

        if (typeIDisposable is null)
        {
            throw new InvalidOperationException("System.IDisposable not found");
        }

        var disposableMethodsWithAttributes = classToProcess.Symbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Method)
            .Cast<IMethodSymbol>()
            .Where(
                m => m.Arity == 0 &&
                     DoesImplementInterface(m.ReturnType, typeIDisposable) &&
                     m.GetAttributes()
                         .Any(
                             a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeOnEnterTreeAttribute) ||
                                  SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeOnReadyAttribute)))
            .Select(m => m.Name)
            .Reverse() // Reverse order of initialization
            .ToList();

        // Nothing to do
        if (!disposableMethodsWithAttributes.Any())
        {
            return classToProcess;
        }

        List<string> methodSources = new List<string>(classToProcess.MethodSources);
        List<MethodCall> methodsToCall = new List<MethodCall>(classToProcess.MethodsToCall);

        methodSources.Add(
            @$"private void __DisposeOnExitTree()
    {{
{string.Join("\n", disposableMethodsWithAttributes.Select(m => $"        __Dispose_{m}();"))}
    }}");
        methodsToCall.Add(new MethodCall("__DisposeOnExitTree", false));

        // Also the new methods we generate
        return new ClassToProcess(
            classToProcess.Syntax,
            classToProcess.Symbol,
            methodsToCall,
            methodSources,
            classToProcess.HasTargetMethodAlready,
            classToProcess.Diagnostics);
    }
}

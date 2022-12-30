using System.Collections;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotHat.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public partial class OnExitTreeGenerator : AbstractNodeNotificationGenerator
{
    protected override string AttributeFullName => "GodotHat.OnExitTreeAttribute";
    protected override string AttributeShortName => "OnExitTree";
    protected override string OverrideEventFunctionName => "_ExitTree";

    protected override bool AllowDisposableReturns => false;

    protected override ClassToProcess? GetNode(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        ClassToProcess? classToProcess = base.GetNode(context, cancellationToken);
        return classToProcess is not null ? this.GetWithExitTreeMethods(context, classToProcess) : null;
    }

    private ClassToProcess? GetWithExitTreeMethods(
        GeneratorSyntaxContext context,
        ClassToProcess classToProcess)
    {
        INamedTypeSymbol typeOnEnterTreeAttribute =
            GetRequiredType(context.SemanticModel, "GodotHat.OnEnterTreeAttribute");
        INamedTypeSymbol typeOnReadyAttribute = GetRequiredType(context.SemanticModel, "GodotHat.OnReadyAttribute");
        INamedTypeSymbol typeIDisposable = GetRequiredType(context.SemanticModel, "System.IDisposable");
        INamedTypeSymbol typeAutoDisposeAttribute =
            GetRequiredType(context.SemanticModel, "GodotHat.AutoDisposeAttribute");

        List<MethodCall> disposeCalls = classToProcess.Symbol.GetMembers()
            .Where(m => m.Kind == SymbolKind.Method)
            .Cast<IMethodSymbol>()
            .Select(
                m => GetMethodCall(
                    classToProcess.Symbol,
                    m,
                    true,
                    typeIDisposable,
                    typeAutoDisposeAttribute,
                    typeOnEnterTreeAttribute,
                    typeOnReadyAttribute))
            .Where(m => m?.ShouldCallDisposable == true)
            .ToList()!;

        // Nothing to do
        if (!disposeCalls.Any())
        {
            return classToProcess;
        }

        List<string> methodSources = new List<string>(classToProcess.MethodSources);
        List<MethodCall> methodsToCall = new List<MethodCall>(classToProcess.MethodsToCall);

        methodSources.Add(
            @$"private void __DisposeOnExitTree()
    {{
{string.Join("\n", disposeCalls.Select(call => $"        {call.DisposeCallString};").Reverse())}
    }}");
        methodsToCall.Add(new MethodCall("__DisposeOnExitTree", MethodCallType.PrimaryEvent));

        // Generate members for any AutoDispose methods without an other event attribute (eg no OnReady)
        IEnumerable<MethodCall> disposables = disposeCalls.Where(call => call.ShouldCallDisposable);

        // TODO: all disposables should be managed here
        foreach (MethodCall call in disposables)
        {
            methodSources.Add($"private IDisposable? {call.DisposableMemberName};");
            if (call.IsAutoDisposable)
            {
                methodSources.Add(
                    $@"public void Update{call.Name}({string.Join(", ", call.Symbol!.Parameters.Select(p => p.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))})
    {{
        {call.DisposableMethodName}();
        {call.DisposableMemberName} = {call.Name}({string.Join(", ", call.Symbol!.Parameters.Select(p => p.Name))});
    }}");
            }
            methodSources.Add(
                @$"private void {call.DisposableMethodName}()
    {{
        {call.DisposableMemberName}?.Dispose();
        {call.DisposableMemberName} = null;
    }}");
        }

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

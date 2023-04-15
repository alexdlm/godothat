using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
        INamedTypeSymbol typeOnEnterTreeAttribute = GeneratorUtil.GetRequiredType(
            context.SemanticModel,
            "GodotHat.OnEnterTreeAttribute");
        INamedTypeSymbol typeOnReadyAttribute =
            GeneratorUtil.GetRequiredType(context.SemanticModel, "GodotHat.OnReadyAttribute");
        INamedTypeSymbol typeIDisposable = GeneratorUtil.GetRequiredType(context.SemanticModel, "System.IDisposable");
        INamedTypeSymbol typeAutoDisposeAttribute = GeneratorUtil.GetRequiredType(
            context.SemanticModel,
            "GodotHat.AutoDisposeAttribute");

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

        List<string> methodSources = new(classToProcess.MethodSources);
        List<MethodCall> methodsToCall = new(classToProcess.MethodsToCall);

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
                AttributeData? autoDisposeAttr = call.Symbol?.GetAttributes()
                    .First(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, typeAutoDisposeAttribute));

                // Check namedArguments first, because constructor has a default value that can be returned in the ConstructorArguments
                var accessibilityValue = autoDisposeAttr?.NamedArguments
                    .Where(na => na.Key == "Accessibility")
                    .Select(na => na.Value)
                    .FirstOrDefault();

                if (!accessibilityValue.HasValue || accessibilityValue.Value.IsNull)
                {
                    accessibilityValue = autoDisposeAttr?.ConstructorArguments.FirstOrDefault();
                }

                string? enumVal = accessibilityValue?.ToCSharpString();

                string accessibility = enumVal switch
                {
                    "GodotHat.Accessibility.Internal" => "internal",
                    "GodotHat.Accessibility.Private" => "private",
                    "GodotHat.Accessibility.Protected" => "protected",
                    _ => "public",
                };

                methodSources.Add(
                    $@"{accessibility} void Update{call.Name}({string.Join(", ", call.Symbol!.Parameters.Select(p => p.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))})
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
            classToProcess.IsTool,
            methodsToCall,
            methodSources,
            classToProcess.HasTargetMethodAlready,
            classToProcess.Diagnostics);
    }
}

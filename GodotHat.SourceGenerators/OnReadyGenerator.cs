using Microsoft.CodeAnalysis;

namespace GodotHat.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class OnReadyGenerator : AbstractNodeNotificationGenerator
{
    protected override string AttributeFullName => "GodotHat.OnReadyAttribute";
    protected override string AttributeShortName => "OnReady";
    protected override string OverrideEventFunctionName => "_Ready";
    protected override bool AllowDisposableReturns => true;
}

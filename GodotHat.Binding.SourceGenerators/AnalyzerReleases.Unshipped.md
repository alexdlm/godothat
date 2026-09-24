; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category         | Severity | Notes
--------|------------------|----------|-------------------------------------------------------
GHB0001 | GodotHat.Binding | Warning  | Reactive view model member has a public setter
GHB0002 | GodotHat.Binding | Info     | View model member type has no Godot Variant conversion
GHB0003 | GodotHat.Binding | Warning  | View model members differ only by case
GHB0004 | GodotHat.Binding | Error    | View model is not accessible
GHB0005 | GodotHat.Binding | Warning  | Generic view models are not supported
GHB0006 | GodotHat.Binding | Warning  | View lists can't be bound

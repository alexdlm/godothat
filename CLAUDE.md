# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

GodotHat is a C# source generator library that provides alternative attribute-based code generation for Godot. It replaces Godot's
built-in ScriptMethodsGenerator and adds convenient lifecycle management attributes for reactive programming patterns.

**Solution Structure:**

- `GodotHat.Attributes/` - Public API (net8.0) defining attributes consumers use
- `GodotHat.SourceGenerators/` - Roslyn incremental generators (netstandard2.0)
- `GodotHat.SourceGenerators.Test/` - xUnit test suite with snapshot testing

## Essential Commands

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build --no-restore

# Run all tests
dotnet test --no-build --verbosity normal

# Build with versioning (CI pattern)
dotnet build --no-restore -p:VersionPrefix="1.0.0" -p:VersionSuffix="alpha"

# Pack NuGet packages
dotnet pack --no-build -c Debug --output nupkgs

# Pack with symbols
dotnet pack --no-build -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg --output nupkgs
```

**Note:** Local builds always use version `0.0.0-local`. CI/CD injects versions from git tags using `git describe`.

## Architecture

### Two-Tier Generator Architecture

**1. Node Lifecycle Generators** (inherit from `AbstractNodeNotificationGenerator`):

- `OnReadyGenerator` - Generates `_Ready()` overrides with IDisposable tracking
- `OnEnterTreeGenerator` - Generates `_EnterTree()` overrides + resolves `[SceneUniqueName]` nodes
- `OnExitTreeGenerator` - Generates `_ExitTree()` overrides with disposal cleanup

**2. Script Methods Generator** (standalone):

- `ScriptMethodsGenerator` - Reimplements Godot's method marshalling system
- Handles bidirectional C# ↔ Godot method invocation
- Filters out generic methods (not supported by Godot)

### Key Utility Files

- `GodotSourceGeneratorsUtil.cs` - Godot type mapping and marshalling logic (Variant system)
- `GeneratorUtil.cs` - Common Roslyn helper functions for symbol traversal
- `Diagnostics.cs` - Standardized error/warning reporting
- `GodotVariantType.cs` - Enum of all Godot variant types with marshalling support

### Generated Code Pattern

All generators produce partial classes that extend user code. Generated code includes:

- Private member fields for disposables (`_godothat_{MemberName}_Disposable`)
- Lifecycle method overrides calling user-decorated methods
- Reverse-order (LIFO) disposal in cleanup methods
- Tool mode support with try-catch wrapping in editor

## Critical Constraints

### Partial Class Requirement

All user classes using GodotHat attributes **must** be declared `partial`:

```csharp
public partial class MyNode : Node { ... }  // ✓ Correct
public class MyNode : Node { ... }          // ✗ Will not generate
```

### Source File Order Sensitivity

`[SceneUniqueName]` fields/properties must appear **before** `[OnEnterTree]` methods that depend on them. The generator resolves
scene-unique nodes during `_EnterTree()`, so dependent code must be ordered correctly in the source file.

### Godot Integration Requirements

Consuming projects must disable Godot's built-in ScriptMethods generator:

```xml
<PropertyGroup>
  <GodotDisabledSourceGenerators>ScriptMethods</GodotDisabledSourceGenerators>
</PropertyGroup>
```

This is automatically added via `GodotHat.SourceGenerators.props` included in the NuGet package.

### Generator Replacement vs Supplementation

**What GodotHat Replaces:**
- `ScriptMethodsGenerator` ONLY - Handles method registration and marshalling (InvokeGodotClassMethod, HasGodotClassMethod, GetGodotMethodList)

**What Godot's Builtin Generators Still Handle:**
- `ScriptPropertiesGenerator` - Property exports (`[Export]` attribute)
- `ScriptSignalsGenerator` - Signal generation (`[Signal]` attribute, delegates, EmitSignal helpers)
- `ScriptSerializationGenerator` - Serialization infrastructure
- `ScriptPropertyDefValGenerator` - Default value tracking
- All other Godot source generators

**Key Insight:** GodotHat is a focused tool that reimplements ONLY the method marshalling system while adding convenient lifecycle management sugar on top. It cooperates with Godot's other generators rather than replacing the entire source generation pipeline. Users still get full property export support, signal generation, and all other Godot features - GodotHat simply enhances the method handling with attributes like `[OnReady]`, `[OnEnterTree]`, and `[SceneUniqueName]`.

### Disposal Lifecycle Management

- Methods decorated with `[OnReady]`, `[OnEnterTree]`, etc. can return `IDisposable` or `IDisposable?`
- Generator creates private fields to track these disposables
- Cleanup occurs in **reverse order** (LIFO) during `_ExitTree()` or disposal
- Supports both `IDisposable` (non-null) and `IDisposable?` (nullable) return types

### Type Marshalling Complexity

The `GodotSourceGeneratorsUtil` handles complex C# → Godot Variant type mapping:

- Primitive types (int, float, bool, string)
- Godot types (Vector2, NodePath, Transform3D, etc.)
- Collections (Array, Dictionary, typed variants like `Godot.Collections.Array<T>`)
- Packed arrays (PackedByteArray, PackedVector3Array, etc.)
- Reference types (Node, Resource, etc.)

Be careful when modifying marshalling logic - it must match Godot's internal expectations.

## Development Patterns

### Versioning Strategy

- Git tags drive version numbers via `git describe`
- Local development always shows `0.0.0-local`
- CI/CD extracts `VersionPrefix` and `VersionSuffix` from tags
- `Common.props` centralizes package metadata

### Source Generator Packaging

Generators are packaged in the `analyzers/dotnet/roslyn4.0` NuGet path. The `.props` file is automatically included and exposes
the `GodotDisabledSourceGenerators` property for downstream projects.

### Testing Approach

Tests use snapshot-style verification:

1. Create in-memory Roslyn compilation with test source code
2. Run generator on compilation
3. Compare full generated output against expected code
4. Use `ReplaceLineEndings()` for cross-platform compatibility

Example from tests:

```csharp
var generator = new OnReadyGenerator();
var compilation = CreateCompilation(testSource);
var result = RunGenerator(compilation, generator);
result.GeneratedSources.Should().ContainSingle()
    .Which.SourceText.ToString().Should().Be(expectedOutput);
```

### Tool Mode Support

Generated code wraps editor-time execution in try-catch blocks when `[Tool]` attribute is present, preventing editor crashes from
runtime exceptions.

## Five Core Attributes

1. **`[OnReady]`** - Executes methods during `_Ready()`, tracks returned IDisposables for cleanup
2. **`[OnEnterTree]` / `[OnExitTree]`** - Lifecycle hooks for tree entry/exit
3. **`[SceneUniqueName]`** - Automatically resolves nodes marked scene-unique in editor (like `%NodeName`)
4. **`[AutoDispose]`** - Ad-hoc disposable tracking without lifecycle method decoration
5. **`[GodotIgnore]`** - Excludes members from Godot's reflection system

See README.md for detailed usage examples and ordering requirements.

## Branch Information

- **Main branch:** `develop`

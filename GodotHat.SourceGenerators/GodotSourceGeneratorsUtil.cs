using Microsoft.CodeAnalysis;

namespace GodotHat.SourceGenerators;

// Code from Godot.SourceGenerators as labeled
// Godot code is MIT licensed:
// Copyright (c) 2014-present Godot Engine contributors (see AUTHORS.md).
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

internal static partial class GodotSourceGeneratorsUtil
{
    public record GodotType(string? NameSpace, string TypeName, GodotVariantType VariantType)
    {
        public static readonly GodotType Void = new(null, "void", GodotVariantType.Nil);

        public string? NameSpace { get; } = NameSpace;
        public string TypeName { get; } = TypeName;
        public string QualifiedName =>
            this.NameSpace is not null ? $"{this.NameSpace}.{this.TypeName}" : this.TypeName;

        public GodotVariantType VariantType { get; } = VariantType;

        // TODO: Add hints if those ever become relevant
    }

    public record GodotArgument(GodotType Type, string Name)
    {
        public GodotType Type { get; } = Type;
        public string Name { get; } = Name;
    }

    public record GodotMethod(
        string Name,
        GodotType? ReturnType,
        IMethodSymbol? Symbol = null,
        List<GodotArgument>? Arguments = null,
        List<Diagnostic>? Diagnostics = null)
    {
        public string Name { get; } = Name;
        public GodotType? ReturnType { get; } = ReturnType;
        public IMethodSymbol? Symbol { get; } = Symbol;
        public List<Diagnostic>? Diagnostics { get; } = Diagnostics;
        public List<GodotArgument>? Arguments { get; } = Arguments;
    }


    // Derived from Godot.SourceGenerators ConvertManagedTypeToMarshalType
    public static GodotType? GetGodotType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            return null;
        }

        var variantType = typeSymbol.SpecialType switch
        {
            SpecialType.System_Boolean => GodotVariantType.Bool,
            SpecialType.System_Byte => GodotVariantType.Int,
            SpecialType.System_Char => GodotVariantType.Int,
            SpecialType.System_Double => GodotVariantType.Float,
            SpecialType.System_Int16 => GodotVariantType.Int,
            SpecialType.System_Int32 => GodotVariantType.Int,
            SpecialType.System_Int64 => GodotVariantType.Int,
            SpecialType.System_SByte => GodotVariantType.Int,
            SpecialType.System_Single => GodotVariantType.Float,
            SpecialType.System_String => GodotVariantType.String,
            SpecialType.System_UInt16 => GodotVariantType.Int,
            SpecialType.System_UInt32 => GodotVariantType.Int,
            SpecialType.System_UInt64 => GodotVariantType.Int,
            _ => typeSymbol.TypeKind switch
            {
                TypeKind.Array when typeSymbol is IArrayTypeSymbol { Rank: 1, ElementType: var elementType } =>
                    ConvertArrayElementTypeToVariantType(elementType),
                TypeKind.Enum => GodotVariantType.Int,
                TypeKind.Struct => ConvertStructTypeToVariantType(typeSymbol),
                _ when IsDescendedFromGodotObject(typeSymbol) => GodotVariantType.Object,
                _ when IsAssemblyAndNamespace(typeSymbol, "GodotSharp", "Godot.Collections") => typeSymbol switch
                {
                    // TODO: we might care if it's generic
                    { Name: "Array" } => GodotVariantType.Array,
                    { Name: "Dictionary" } => GodotVariantType.Dictionary,
                    _ => null,
                },
                _ => null,
            },
        };

        return variantType is null
            ? null
            : new GodotType(
                typeSymbol.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                typeSymbol.Name,
                (GodotVariantType)variantType);
    }

    public static bool IsAssemblyAndNamespace(ITypeSymbol typeSymbol, string assemblyName, string @namespace) =>
        typeSymbol.ContainingAssembly?.Name == assemblyName &&
        typeSymbol.ContainingNamespace?.Name == @namespace;

    private static GodotVariantType? ConvertStructTypeToVariantType(ITypeSymbol typeSymbol)
    {
        if (!IsAssemblyAndNamespace(typeSymbol, "GodotSharp", "Godot"))
        {
            return null;
        }

        return typeSymbol.Name switch
        {
            "Aabb" => GodotVariantType.Aabb,
            "Basis" => GodotVariantType.Basis,
            "Callable" => GodotVariantType.Callable,
            "Color" => GodotVariantType.Color,
            "Plane" => GodotVariantType.Plane,
            "Projection" => GodotVariantType.Projection,
            "Quaternion" => GodotVariantType.Quaternion,
            "Rect2" => GodotVariantType.Rect2,
            "Rect2I" => GodotVariantType.Rect2I,
            "Rid" => GodotVariantType.Rid,
            "Signal" => GodotVariantType.Signal,
            "Transform2D" => GodotVariantType.Transform2D,
            "Transform3D" => GodotVariantType.Transform3D,
            "Variant" => GodotVariantType.Nil,
            "Vector2" => GodotVariantType.Vector2,
            "Vector2I" => GodotVariantType.Vector2I,
            "Vector3" => GodotVariantType.Vector3,
            "Vector3I" => GodotVariantType.Vector3I,
            "Vector4" => GodotVariantType.Vector4,
            "Vector4I" => GodotVariantType.Vector4I,
            _ => null,
        };
    }

    private static bool IsDescendedFromGodotObject(ITypeSymbol typeSymbol)
    {
        return GeneratorUtil.GetThisAndBaseTypes(typeSymbol)
            .Cast<INamedTypeSymbol>()
            .Any(s => s.Name == "GodotObject" && IsAssemblyAndNamespace(s, "GodotSharp", "Godot"));
    }


    private static GodotVariantType? ConvertArrayElementTypeToVariantType(ITypeSymbol typeSymbol)
    {
        if (IsDescendedFromGodotObject(typeSymbol))
        {
            return GodotVariantType.Array;
        }

        return typeSymbol.SpecialType switch
        {
            SpecialType.System_Byte => GodotVariantType.PackedByteArray,
            SpecialType.System_Double => GodotVariantType.PackedFloat64Array,
            SpecialType.System_Int32 => GodotVariantType.PackedInt32Array,
            SpecialType.System_Int64 => GodotVariantType.PackedInt64Array,
            SpecialType.System_Single => GodotVariantType.PackedFloat32Array,
            SpecialType.System_String => GodotVariantType.PackedStringArray,
            _ when !IsAssemblyAndNamespace(typeSymbol, "GodotSharp", "Godot") => typeSymbol.Name switch
            {
                "Color" => GodotVariantType.PackedColorArray,
                "NodePath" => GodotVariantType.Array,
                "Rid" => GodotVariantType.Array,
                "StringName" => GodotVariantType.Array,
                "Vector2" => GodotVariantType.PackedVector2Array,
                "Vector3" => GodotVariantType.PackedVector3Array,
                _ => null,
            },
            _ => null,
        };
    }

    public static INamedTypeSymbol? GetGodotParentClass(INamedTypeSymbol classSymbol)
    {
        return GeneratorUtil.GetThisAndBaseTypes(classSymbol)
            .Cast<INamedTypeSymbol>()
            .First(type => type.ContainingAssembly?.Name == "GodotSharp" && type.ContainingNamespace?.Name == "Godot");
    }
}

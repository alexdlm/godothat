using Microsoft.CodeAnalysis;

namespace GodotHat.SourceGenerators;

internal static class GeneratorUtil
{
    public static IEnumerable<ITypeSymbol> GetThisAndBaseTypes(ITypeSymbol? symbol)
    {
        if (symbol is null)
        {
            yield break;
        }

        ITypeSymbol? current = symbol;
        do
        {
            yield return current;
            current = current.BaseType;
        } while (current is not null);
    }

    public static bool DoesExtendClass(ITypeSymbol? symbol, ISymbol typeNodeClass)
    {
        return GetThisAndBaseTypes(symbol).Any(t => SymbolEqualityComparer.Default.Equals(t, typeNodeClass));
    }

    public static bool DoesImplementInterface(ITypeSymbol? symbol, ISymbol typeNodeInterface)
    {
        if (symbol is null)
        {
            return false;
        }
        return SymbolEqualityComparer.Default.Equals(symbol, typeNodeInterface) ||
               symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, typeNodeInterface));
    }

    public static INamedTypeSymbol GetRequiredType(SemanticModel model, string typeName)
    {
        INamedTypeSymbol? typeSymbol = model.Compilation.GetTypeByMetadataName(typeName);
        if (typeSymbol is null)
        {
            throw new InvalidOperationException($"Failed to resolve {typeName}, is it in a referenced assembly?");
        }
        return typeSymbol;
    }

    public static bool TypeIsOneOf(
        ITypeSymbol typeSymbol,
        string assemblyName,
        string @namespace,
        HashSet<string> names) => typeSymbol.ContainingAssembly?.Name == assemblyName &&
                                  typeSymbol.ContainingNamespace.Name == @namespace &&
                                  names.Contains(typeSymbol.Name);
}

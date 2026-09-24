using FluentAssertions;
using Godot;

namespace GodotHat.SourceGenerators.Test;

public class GodotVariantTypeTest
{
    [Fact]
    public void MatchesGodotVariantType()
    {
        Enum.GetNames<GodotVariantType>()
            .ToDictionary(name => name, name => (long)Enum.Parse<GodotVariantType>(name))
            .Should()
            .Equal(Enum.GetNames<Variant.Type>().ToDictionary(name => name, name => (long)Enum.Parse<Variant.Type>(name)));
    }
}

using AwesomeAssertions;
using GodotHat.Binding.Core.Test.Support;

namespace GodotHat.Binding.Core.Test;

public class AuthoringTest
{
    private const long Default = 6; // storage | editor
    private static readonly string[] ButtonClasses = ["Button", "BaseButton", "Control", "CanvasItem", "Node", "Object"];

    private static TargetPropertyInfo Property(string name, int variantType = 4, long usage = Default, int hint = 0, TargetMarker marker = TargetMarker.None) =>
        new(ButtonClasses, name, variantType, hint, usage, marker);

    [Theory]
    [InlineData("text", true)]
    [InlineData("visible", true)]
    [InlineData("disabled", true)]
    [InlineData("modulate", true)]
    [InlineData("theme_override_colors/font_color", true)]
    [InlineData("offset_transform_position", true)]
    [InlineData("anchor_left", false)]
    [InlineData("anchors_preset", false)]
    [InlineData("offset_left", false)]
    [InlineData("layout_mode", false)]
    [InlineData("size_flags_horizontal", false)]
    [InlineData("focus_next", false)]
    [InlineData("process_mode", false)]
    [InlineData("editor_description", false)]
    [InlineData("metadata/godothat_bindings", false)]
    [InlineData("popup/item_0/text", false)]
    [InlineData("pivot_offset_ratio", false)]
    public void DefaultRules(string name, bool bindable) =>
        TargetRules.Default.IsBindable(Property(name), out _).Should().Be(bindable);

    [Fact]
    public void UsageFlagsHideInternalAndUnshownProperties()
    {
        TargetRules.Default.IsBindable(Property("text", usage: 2), out string reason).Should().BeFalse();
        reason.Should().Be("it isn't shown in the editor");
        TargetRules.Default.IsBindable(Property("script", usage: Default | 8), out _).Should().BeFalse();
        TargetRules.Default.IsBindable(Property("Layout", usage: 64), out _).Should().BeFalse();
    }

    [Fact]
    public void NodeReferencesAndCallablesAreNotTargets()
    {
        TargetRules.Default.IsBindable(Property("shortcut_context_path", variantType: 22), out _).Should().BeFalse();
        TargetRules.Default.IsBindable(Property("target", variantType: 24, hint: 34), out _).Should().BeFalse();
        TargetRules.Default.IsBindable(Property("icon", variantType: 24, hint: 17), out _).Should().BeTrue();
    }

    [Fact]
    public void ProjectRulesDenyAndReenable()
    {
        var rules = new TargetRules(deny: ["Button.text", "*.modulate"], allow: ["Control.layout_direction"]);

        rules.IsBindable(Property("text"), out string reason).Should().BeFalse();
        reason.Should().Be("the project's binding rules deny it");
        rules.IsBindable(Property("modulate"), out _).Should().BeFalse();
        rules.IsBindable(Property("layout_direction"), out _).Should().BeTrue("allow re-enables what the defaults hide");
        rules.IsBindable(Property("layout_mode"), out _).Should().BeFalse();
        new TargetRules(deny: ["Label.text"]).IsBindable(Property("text"), out _).Should().BeTrue("the rule is for another class");
    }

    [Fact]
    public void MarkersOverrideEverything()
    {
        TargetRules.Default.IsBindable(Property("text", marker: TargetMarker.NotBindable), out string reason).Should().BeFalse();
        reason.Should().Be("it is marked [NotBindable]");
        TargetRules.Default.IsBindable(Property("Secret", usage: 4096, marker: TargetMarker.BindableTarget), out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("*", "anything", true)]
    [InlineData("anchor_*", "anchor_left", true)]
    [InlineData("anchor_*", "anchors_preset", false)]
    [InlineData("accessibility_*_nodes", "accessibility_labeled_by_nodes", true)]
    [InlineData("a*b*c", "axxbyyc", true)]
    [InlineData("a*b*c", "axxbyy", false)]
    public void Globs(string pattern, string text, bool matches) => TargetRules.Glob(pattern, text).Should().Be(matches);

    [Fact]
    public void SuggestsCompatiblePathsFirstWithConverters()
    {
        IReadOnlyList<PathSuggestion> text = PathSuggester.Suggest(typeof(PersonViewModel), BindingKind.Property, TargetTypeInfo.String);

        text.TakeWhile(s => s.Compatibility == PathCompatibility.Compatible).Select(s => s.Path)
            .Should().StartWith(["Name", "Messages", "Id"]).And.Contain("Friend.Name");
        PathSuggestion age = text.Single(s => s.Path == "Age");
        age.Compatibility.Should().Be(PathCompatibility.NeedsConverter);
        age.Converters.Should().Equal("to_string", "format");
        text.Single(s => s.Path == "Mood").Converters.Should().Equal("enum_name", "to_string");
        text.Single(s => s.Path == "Greet").Compatibility.Should().Be(PathCompatibility.Incompatible);
        text.Last().Compatibility.Should().Be(PathCompatibility.Incompatible);
    }

    [Fact]
    public void SuggestsNullChecksAndAnyForVisibility()
    {
        IReadOnlyList<PathSuggestion> visible = PathSuggester.Suggest(typeof(PersonViewModel), BindingKind.Property, TargetTypeInfo.Bool, maxDepth: 0);

        visible.Single(s => s.Path == "IsAdult").Compatibility.Should().Be(PathCompatibility.Compatible);
        visible.Single(s => s.Path == "Friend").Converters.Should().Equal("not_null", "is_null");
        visible.Single(s => s.Path == "Age").Converters.Should().Equal("any");
        visible.Should().NotContain(s => s.Path.StartsWith("Friend.", StringComparison.Ordinal), "depth 0 doesn't follow view models");
    }

    [Fact]
    public void SuggestsByBindingKind()
    {
        PathSuggester.Suggest(typeof(PersonViewModel), BindingKind.Command)
            .Where(s => s.Compatibility == PathCompatibility.Compatible).Select(s => s.Path)
            .Should().Equal("Greet", "AddYears", "Friend.Greet", "Friend.AddYears", "Friend.Friend.Greet", "Friend.Friend.AddYears");
        PathSuggester.Suggest(typeof(InventoryViewModel), BindingKind.Items, maxDepth: 0)
            .Where(s => s.Compatibility == PathCompatibility.Compatible).Select(s => s.Path)
            .Should().Equal("Items", "Tags", "Stock", "Queue", "Stack", "Recent", "Shouted");
        PathSuggester.Suggest(typeof(TeamViewModel), BindingKind.Context, maxDepth: 0)
            .Where(s => s.Compatibility == PathCompatibility.Compatible).Select(s => s.Path)
            .Should().Equal("Leader", "Captain");
    }

    [Fact]
    public void CollectionsBindTheirCount()
    {
        PathSuggester.Check(typeof(int), isCollection: true, TargetTypeInfo.Int, out _).Should().Be(PathCompatibility.Compatible);
        PathSuggester.Check(typeof(int), isCollection: true, TargetTypeInfo.Bool, out IReadOnlyList<string> converters).Should().Be(PathCompatibility.NeedsConverter);
        converters.Should().Equal("any");
    }
}

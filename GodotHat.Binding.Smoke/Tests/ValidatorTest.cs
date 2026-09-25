namespace GodotHat.Binding.Smoke.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using GdUnit4;
using Godot;
using GodotHat.Binding.Smoke.ViewModels;
using static Mount;

[TestSuite]
[RequireGodotRuntime]
public class ValidatorTest
{
    private static BindingReport Report(IEnumerable<BindingReport> reports, string node, string key) =>
        reports.Single(r => r.NodePath == node && r.Key == key);

    private static PackedScene Pack(Node root)
    {
        foreach (Node child in root.FindChildren("*", owned: false))
        {
            child.Owner = root;
        }

        var scene = new PackedScene();
        scene.Pack(root);
        root.Free();
        return scene;
    }

    [TestCase]
    public void ChecksPathsConvertersAndTargets()
    {
        var root = new BindingRoot { Name = "Root", ViewModelType = typeof(FormViewModel).FullName! }.With(
            new Label { Name = "Good" }.Bind("text", Def("Name")),
            new Label { Name = "Missing" }.Bind("text", Def("Nope")),
            new Label { Name = "NeedsConverter" }.Bind("text", Def("Count")),
            new Label { Name = "Converted" }.Bind("text", Def("Count", converter: "format", argument: "{0}")),
            new Label { Name = "UnknownConverter" }.Bind("text", Def("Count", converter: "nope")),
            new Label { Name = "Denied" }.Bind("anchor_left", Def("Volume")),
            new Label { Name = "NoProperty" }.Bind("nope", Def("Name")),
            new Button { Name = "Command" }.Bind("pressed", Command("Increment")).Bind("toggled", Command("Name")),
            new Button { Name = "NoSignal" }.Bind("nope", Command("Increment")),
            new Label { Name = "Event" }.Bind("set_text", Def("Flashed", kind: BindingKind.Event)).Bind("nope", Def("Flashed", kind: BindingKind.Event)),
            new CheckBox { Name = "Visible" }.Bind("visible", Def("Child")).Bind("disabled", Def("Child", converter: "is_null")),
            new PanelContainer { Name = "Panel" }.Bind(BindingRootBase.ContextKey, Def("Child")).With(new Label { Name = "Nested" }.Bind("text", Def("Name"))));

        IReadOnlyList<BindingReport> reports = BindingValidator.Validate(root);
        root.Free();

        Report(reports, "Good", "text").Status.Should().Be(BindingStatus.Ok);
        Report(reports, "Missing", "text").Message.Should().Be("FormViewModel has no bindable member 'Nope'.");
        Report(reports, "NeedsConverter", "text").Message.Should().Be("'text' can't take Int32 directly; add a converter such as to_string or format.");
        Report(reports, "Converted", "text").Status.Should().Be(BindingStatus.Ok);
        Report(reports, "UnknownConverter", "text").Message.Should().Be("Unknown converter 'nope'.");
        Report(reports, "Denied", "anchor_left").Status.Should().Be(BindingStatus.Warning);
        Report(reports, "NoProperty", "nope").Message.Should().Be("Label has no property 'nope'.");
        Report(reports, "Command", "pressed").Status.Should().Be(BindingStatus.Ok);
        Report(reports, "Command", "toggled").Message.Should().Be("'Name' is not a command.");
        Report(reports, "NoSignal", "nope").Message.Should().Be("Button has no signal 'nope'.");
        Report(reports, "Event", "set_text").Status.Should().Be(BindingStatus.Ok);
        Report(reports, "Event", "nope").Message.Should().Be("Label has no method 'nope'.");
        Report(reports, "Visible", "visible").Message.Should().Be("'visible' can't take FormViewModel directly; add a converter such as not_null or is_null.");
        Report(reports, "Visible", "disabled").Status.Should().Be(BindingStatus.Ok);
        Report(reports, "Panel", "@context").Status.Should().Be(BindingStatus.Ok);
        Report(reports, "Panel/Nested", "text").Status.Should().Be(BindingStatus.Ok, "the panel's context is a FormViewModel too");
    }

    [TestCase]
    public void ChecksItemTemplatesAgainstTheItemType()
    {
        var row = new Button { Name = "Row" }.Bind("text", Def("Name")).Bind("tooltip_text", Def("Missing"));
        var root = new BindingRoot { Name = "Root", ViewModelType = typeof(TodoListViewModel).FullName! }.With(
            new VBoxContainer { Name = "List" }.Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items", Template = Pack(row) }),
            new VBoxContainer { Name = "NoTemplate" }.Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items" }),
            new ItemList { Name = "Names" }.Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items", ItemText = "Nope" }).Bind(BindingRootBase.SelectedKey, Def("Selected", BindingMode.TwoWay)),
            new VBoxContainer { Name = "NotItems" }.Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Selected" }));

        IReadOnlyList<BindingReport> reports = BindingValidator.Validate(root);
        root.Free();

        Report(reports, "List", "@items").Status.Should().Be(BindingStatus.Ok);
        Report(reports, ".", "text").Status.Should().Be(BindingStatus.Ok, "the template's context is a TodoItem");
        Report(reports, ".", "tooltip_text").Message.Should().Be("TodoItem has no bindable member 'Missing'.");
        Report(reports, "NoTemplate", "@items").Message.Should().Be("'@items' on VBoxContainer needs a Template scene.");
        Report(reports, "Names", "ItemText").Message.Should().Be("TodoItem has no bindable member 'Nope'.");
        Report(reports, "Names", "@selected").Status.Should().Be(BindingStatus.Ok);
        Report(reports, "NotItems", "@items").Message.Should().Be("Selected is not a collection.");
    }

    [TestCase]
    public void UndeclaredContextsAreUnchecked()
    {
        var root = new BindingRoot { Name = "Root" }.With(new Label { Name = "Title" }.Bind("text", Def("Anything")));

        BindingReport report = Report(BindingValidator.Validate(root), "Title", "text");
        root.Free();

        report.Status.Should().Be(BindingStatus.Unchecked);
        report.Message.Should().Contain("set ViewModelType");
    }

    [TestCase]
    public void ContextTypeFollowsDeclaredTypesAndContextBindings()
    {
        var nested = new Label();
        var panel = new PanelContainer().Bind(BindingRootBase.ContextKey, Def("Child")).With(nested);
        var root = new BindingRoot { ViewModelType = typeof(FormViewModel).FullName! }.With(panel);

        BindingValidator.ContextTypeAt(root).Should().BeNull("the root's own bindings resolve against its parent's context");
        BindingValidator.ContextTypeAt(root, children: true).Should().Be(typeof(FormViewModel));
        BindingValidator.ContextTypeAt(nested).Should().Be(typeof(FormViewModel));
        var unbound = new Label();
        BindingValidator.ContextTypeAt(unbound).Should().BeNull();
        unbound.Free();
        root.Free();
    }

    [TestCase]
    public async Task NestedRootsInheritTheirDeclaredTypeOrCreateTheirOwn()
    {
        using var vm = new FormViewModel();
        var same = new BindingRoot { ViewModelType = typeof(FormViewModel).FullName! };
        var other = new BindingRoot { ViewModelType = typeof(CounterViewModel).FullName! };
        var root = await AddToTree(new BindingRoot { Context = vm }.With(same, other));

        same.ViewModel.Should().BeNull("a component of the declared type uses the context it's placed in");
        same.DataContext!.Type.Should().Be(typeof(FormViewModel));
        other.ViewModel.Should().BeOfType<CounterViewModel>("a component of another type creates its own");
        await Unmount(root);
    }

    [TestCase]
    public void ValidatesSavedScenesAndProjects()
    {
        const string directory = "user://validator_project";
        DirAccess.MakeDirRecursiveAbsolute(directory + "/ui");
        ResourceSaver.Save(Pack(new BindingRoot { Name = "Good", ViewModelType = typeof(FormViewModel).FullName! }.With(new Label { Name = "Title" }.Bind("text", Def("Name")))), directory + "/ui/good.tscn");
        ResourceSaver.Save(Pack(new BindingRoot { Name = "Broken", ViewModelType = typeof(FormViewModel).FullName! }.With(new Label { Name = "Title" }.Bind("text", Def("Renamed")))), directory + "/broken.tscn");

        IReadOnlyList<BindingReport> reports = BindingValidator.ValidateProject(directory);

        reports.Should().HaveCount(2);
        BindingReport broken = reports.Single(r => r.Status == BindingStatus.Error);
        broken.Scene.Should().Be(directory + "/broken.tscn");
        broken.NodePath.Should().Be("Title");
        broken.ToString().Should().Be($"Error: {directory}/broken.tscn:Title text = 'Renamed': FormViewModel has no bindable member 'Renamed'.");

        using (FileAccess file = FileAccess.Open(directory + "/corrupt.tscn", FileAccess.ModeFlags.Write))
        {
            file.StoreString("not a scene");
        }

        BindingReport corrupt = BindingValidator.ValidateScene(directory + "/corrupt.tscn").Should().ContainSingle().Subject;
        corrupt.Status.Should().Be(BindingStatus.Error);
        corrupt.Message.Should().Be("The scene can't be loaded.");
        DirAccess.RemoveAbsolute(directory + "/corrupt.tscn");
    }

    [TestCase]
    public void ValidatesTheWholeProjectByDefault()
    {
        IReadOnlyList<BindingReport> reports = BindingValidator.ValidateProject();

        reports.Should().Contain(r => r.Scene == "res://editor_smoke/Screen.tscn" && r.NodePath == "Broken" && r.Status == BindingStatus.Error);
    }
}

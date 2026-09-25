namespace GodotHat.Binding.Smoke.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using GdUnit4;
using Godot;
using GodotHat.Binding.Smoke.ViewModels;
using GodotHat.Binding.Testing;
using static Mount;

[TestSuite]
[RequireGodotRuntime]
public class HarnessTest
{
    private const string Directory = "user://harness";

    private static BindingRoot Form() => new BindingRoot { Name = "Form" }.With(
        new VBoxContainer { Name = "Box" }.With(
            new Label { Name = "Title" }.Bind("text", Def("Name")),
            new LineEdit { Name = "NameEdit" }.Bind("text", Def("Name", BindingMode.TwoWay)),
            new HSlider { Name = "Volume", MaxValue = 100 }.Bind("value", Def("Volume", BindingMode.TwoWay)),
            new CheckBox { Name = "Enabled" }.Bind("button_pressed", Def("Enabled", BindingMode.TwoWay)),
            new OptionButton { Name = "Speed" }.Bind("selected", Def("Speed", BindingMode.TwoWay)),
            new Label { Name = "Count" }.Bind("text", Def("Count", converter: "to_string")),
            new Button { Name = "Increment" }.Bind("pressed", Command("Increment")),
            new Button { Name = "Reset" }.Bind("pressed", Command("Reset"))));

    // Owns the scene's own descendants for packing, but not Controls' internal children, eg an OptionButton's popup.
    private static void Own(Node root, Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = root;
            Own(root, child);
        }
    }

    private static PackedScene Pack(Node root, string? path = null)
    {
        Own(root, root);

        var scene = new PackedScene();
        scene.Pack(root);
        root.Free();
        if (path is null)
        {
            return scene;
        }

        DirAccess.MakeDirRecursiveAbsolute(Directory);
        ResourceSaver.Save(scene, path);
        return GD.Load<PackedScene>(path);
    }

    [TestCase]
    public async Task DrivesControlsThroughRealInput()
    {
        using var vm = new FormViewModel();
        await using BindingHarness ui = await BindingHarness.Mount(Form(), vm);

        await ui.Bound<LineEdit>("Name").Type("Ada");
        vm.Name.Value.Should().Be("Ada");
        ui.Bound<Label>("Name").AssertText("Ada");

        await ui.Bound<HSlider>("Volume").SetValue(40);
        vm.Volume.Value.Should().Be(40);

        await ui.Bound<CheckBox>("Enabled").SetPressed();
        vm.Enabled.Value.Should().BeTrue();
        ui.Bound<CheckBox>("Enabled").AssertPressed();

        await ui.Bound<OptionButton>("Speed").Select("Fast");
        vm.Speed.Value.Should().Be(Speed.Fast);
        ui.Bound<OptionButton>("Speed").AssertSelected("Fast").AssertSelected(1);

        ui.Bound<Button>("Reset").AssertDisabled();
        await ui.Bound<Button>("Increment").Press();
        ui.Bound<Label>("Count").AssertText("1");
        ui.Bound<Button>("Reset").AssertEnabled();
        await ui.Bound<Button>("Reset").Press();
        vm.Count.Value.Should().Be(0);
        ui.AssertNoBindingErrors();
    }

    [TestCase]
    public async Task TypingFollowsTheControlsRules()
    {
        using var vm = new FormViewModel();
        var limited = new LineEdit { Name = "Limited", MaxLength = 3 }.Bind("text", Def("Name", BindingMode.TwoWay));
        string? submitted = null;
        limited.TextSubmitted += text => submitted = text;
        var root = new BindingRoot().With(
            limited,
            new SpinBox { Name = "Spin", MaxValue = 100 }.Bind("value", Def("Volume", BindingMode.TwoWay)),
            new LineEdit { Name = "ReadOnly", Editable = false }.Bind("text", Def("Count", converter: "to_string")));
        await using BindingHarness ui = await BindingHarness.Mount(root, vm);

        await ui.Bound<LineEdit>("Name").Type("Grace");
        vm.Name.Value.Should().Be("Gra", "typing stops at the max length");

        await ui.Bound<LineEdit>("Name").Type("Al", submit: true);
        vm.Name.Value.Should().Be("Al");
        submitted.Should().Be("Al");

        await ui.Bound<LineEdit>("Name").Type("");
        vm.Name.Value.Should().Be("");

        await ui.Bound<SpinBox>("Volume").Type("42");
        vm.Volume.Value.Should().Be(42);

        Func<Task> typeReadOnly = () => ui.Bound<LineEdit>("Count").Type("5");
        await typeReadOnly.Should().ThrowAsync<BindingAssertionException>()
            .WithMessage("LineEdit 'ReadOnly' (Count) is read-only, so it can't be typed into.");

        using var notesVm = new FormViewModel();
        await using BindingHarness notes = await BindingHarness.Mount(
            new BindingRoot().With(new TextEdit { Name = "Notes" }.Bind("text", Def("Name", BindingMode.TwoWay))),
            notesVm);
        await notes.Bound<TextEdit>("Name").Type("one\ntwo");
        notesVm.Name.Value.Should().Be("one\ntwo");
    }

    [TestCase]
    public async Task FailsWhenAPlayerCouldNot()
    {
        using var vm = new FormViewModel();
        BindingRoot root = Form();
        root.GetNode<Button>("Box/Increment").FocusMode = Control.FocusModeEnum.None;
        root.GetNode<Label>("Box/Count").Visible = false;
        await using BindingHarness ui = await BindingHarness.Mount(root, vm);

        Func<Task> pressDisabled = () => ui.Bound<Button>("Reset").Press();
        await pressDisabled.Should().ThrowAsync<BindingAssertionException>()
            .WithMessage("Button 'Box/Reset' (Reset) is disabled, so it can't be pressed.");

        Func<Task> typeIntoLabel = () => ui.Bound<Label>("Name").Type("x");
        await typeIntoLabel.Should().ThrowAsync<BindingAssertionException>()
            .WithMessage("*isn't a LineEdit, TextEdit or SpinBox*");

        ui.Bound<Label>("Count").AssertHidden();
        Action visible = () => ui.Bound<Label>("Count").AssertVisible();
        visible.Should().Throw<BindingAssertionException>().WithMessage("Label 'Box/Count' (Count) expected to be visible, but was hidden.");

        // Buttons players click but can't focus still press, and focus stays where it was
        LineEdit nameEdit = ui.Bound<LineEdit>("Name");
        nameEdit.GrabFocus();
        await ui.Bound<Button>("Increment").Press();
        vm.Count.Value.Should().Be(1);
        ui.Bound<Button>("Increment").Control.FocusMode.Should().Be(Control.FocusModeEnum.None);
        nameEdit.HasFocus().Should().BeTrue();
    }

    [TestCase]
    public async Task FindsControlsByWhatTheyBindTo()
    {
        using var vm = new FormViewModel();
        await using BindingHarness ui = await BindingHarness.Mount(Form(), vm);

        ui.Bound<LineEdit>("Name", target: "text").Control.Name.ToString().Should().Be("NameEdit");
        ui.Find<Label>("Box/Title").AssertText("");

        Action missing = () => ui.Bound<Label>("Nope");
        missing.Should().Throw<BindingAssertionException>().WithMessage("No Label in BindingRoot 'Form' binds 'Nope'.");
        Action otherType = () => ui.Bound<Button>("Name");
        otherType.Should().Throw<BindingAssertionException>()
            .WithMessage("No Button in BindingRoot 'Form' binds 'Name'. Other nodes bind it: Label 'Box/Title' (text), LineEdit 'Box/NameEdit' (text).");
        Action ambiguous = () => ui.Bound("Name");
        ambiguous.Should().Throw<BindingAssertionException>()
            .WithMessage("2 Controls in BindingRoot 'Form' bind 'Name': Label 'Box/Title', LineEdit 'Box/NameEdit'. *");
        Action wrongType = () => ui.Find<Button>("Box/Title");
        wrongType.Should().Throw<BindingAssertionException>().WithMessage("Label 'Box/Title' is not a Button.");
        Action wrongText = () => ui.Bound<Label>("Name").AssertText("Ada");
        wrongText.Should().Throw<BindingAssertionException>().WithMessage("Label 'Box/Title' (Name) expected text 'Ada', but was ''.");
    }

    [TestCase]
    public async Task FindsControlsInRows()
    {
        using var vm = new TodoListViewModel();
        vm.Items.Add(new TodoItem("a"));
        vm.Items.Add(new TodoItem("b"));
        PackedScene row = Pack(new HBoxContainer { Name = "Row" }.With(
            new Label { Name = "Name" }.Bind("text", Def("Name")),
            new Button { Name = "Remove" }.Bind("pressed", new BindingDef { Kind = BindingKind.Command, Path = "Remove", Source = BindingSourceKind.ParentContext, Parameter = BindingParameterKind.Item })));
        var root = new BindingRoot().With(
            new VBoxContainer { Name = "List" }.With(new Label { Name = "Header" })
                .Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items", Template = row }));
        await using BindingHarness ui = await BindingHarness.Mount(root, vm);

        BoundItems items = ui.Items("Items").AssertCount(2);
        items.Row(1).Bound<Label>("Name").AssertText("b");
        await items.Row(0).Bound<Button>("Remove").Press();
        vm.Items.Should().ContainSingle().Which.Name.Value.Should().Be("b");
        items.AssertCount(1).Row(0).Bound<Label>("Name").AssertText("b");

        Action noRow = () => items.Row(1);
        noRow.Should().Throw<BindingAssertionException>().WithMessage("VBoxContainer 'List' (Items) has 1 rows, so there's no row 1.");
        ui.AssertNoBindingErrors();
    }

    [TestCase]
    public async Task SelectsFromListControls()
    {
        using var vm = new TodoListViewModel();
        var a = new TodoItem("a");
        var b = new TodoItem("b");
        vm.Items.AddRange([a, b]);
        var root = new BindingRoot().With(
            new ItemList { Name = "List" }
                .Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items", ItemText = "Name" })
                .Bind(BindingRootBase.SelectedKey, Def("Selected", BindingMode.TwoWay)));
        await using BindingHarness ui = await BindingHarness.Mount(root, vm);

        ui.Items("Items").AssertTexts("a", "b").AssertSelected(-1).AssertCount(2);
        await ui.Items("Items").Select("b");
        vm.Selected.Value.Should().BeSameAs(b);

        vm.Selected.Value = a;
        ui.Items("Items").AssertSelected(0);
        ui.AssertNoBindingErrors();
    }

    [TestCase]
    public async Task FollowsWhatPresentersShow()
    {
        using var shell = new ShellViewModel();
        PackedScene page = Pack(new Label { Name = "Page" }.Bind("text", Def("Title")), Directory + "/page.tscn");
        var root = new BindingRoot().With(
            new ContentPresenter { Name = "Pages", Template = page, OwnsContent = true }.Bind(BindingRootBase.ContextKey, Def("Page")));
        await using BindingHarness ui = await BindingHarness.Mount(root, shell);

        ui.Presenter("Page").AssertEmpty();
        shell.Page.Value = new HomePageViewModel();
        var home = ui.Presenter().AssertShowing<HomePageViewModel>();
        ui.Presenter().AssertScene(Directory + "/page.tscn").Bound<Label>("Title").AssertText("Home");

        shell.Page.Value = new AboutPageViewModel();
        ui.Presenter().AssertShowing<AboutPageViewModel>();
        home.IsDisposed.Should().BeTrue();
        Action wrongPage = () => ui.Presenter().AssertShowing<HomePageViewModel>();
        wrongPage.Should().Throw<BindingAssertionException>()
            .WithMessage("ContentPresenter 'Pages' expected to show a HomePageViewModel, but shows AboutPageViewModel.");
    }

    [TestCase]
    public async Task SuppliesServicesAndSamples()
    {
        IViewModelActivator before = GodotBinding.Activator;
        var greeting = new BindingRoot { ViewModelType = typeof(GreetingViewModel).FullName! }
            .With(new Label { Name = "Greeting" }.Bind("text", Def("Greeting")));
        await using (BindingHarness ui = await BindingHarness.Mount(greeting, services: new SmokeServices()))
        {
            ui.ViewModel.Should().BeOfType<GreetingViewModel>();
            ui.Bound<Label>("Greeting").AssertText("Hello, Godot");
            GodotBinding.Activator.Should().NotBeSameAs(before);
        }

        GodotBinding.Activator.Should().BeSameAs(before);

        DirAccess.MakeDirRecursiveAbsolute(Directory);
        ResourceSaver.Save(new FormSample { Name = "From sample" }, Directory + "/form_sample.tres");
        FormViewModel sampled;
        await using (BindingHarness ui = await BindingHarness.Mount(Form(), sample: Directory + "/form_sample.tres"))
        {
            ui.Bound<Label>("Name").AssertText("From sample");
            sampled = (FormViewModel)ui.ViewModel!;
        }

        sampled.IsDisposed.Should().BeTrue("the harness owns view models it creates from samples");

        Func<Task> both = () => BindingHarness.Mount(new BindingRoot(), activator: DefaultViewModelActivator.Instance, services: new SmokeServices());
        await both.Should().ThrowAsync<ArgumentException>();
    }

    [TestCase]
    public async Task MountsScenesByPath()
    {
        Pack(Form(), Directory + "/form.tscn");
        using var vm = new FormViewModel();
        vm.Name.Value = "Saved";
        await using BindingHarness ui = await BindingHarness.Mount(Directory + "/form.tscn", vm);

        ui.Scene.SceneFilePath.Should().Be(Directory + "/form.tscn");
        ui.Bound<Label>("Name").AssertText("Saved");
    }

    [TestCase]
    public async Task CollectsErrorsAndWaitsForOtherThreads()
    {
        using var vm = new FormViewModel();
        BindingRoot root = Form().With(new Label { Name = "Broken" }.Bind("text", Def("Nope")));
        await using BindingHarness ui = await BindingHarness.Mount(root, vm);

        ui.Errors.Should().ContainSingle().Which.Message.Should().Be("FormViewModel has no bindable member 'Nope'.");
        Action noErrors = ui.AssertNoBindingErrors;
        noErrors.Should().Throw<BindingAssertionException>().WithMessage("1 binding error(s):*has no bindable member 'Nope'.");

        var worker = new Thread(() => vm.Count.Value = 5);
        worker.Start();
        worker.Join();
        await ui.WaitUntil(() => ui.Bound<Label>("Count").Control.Text == "5");

        Func<Task> timeout = () => ui.WaitUntil(() => vm.Count.Value > 5, TimeSpan.FromMilliseconds(50));
        await timeout.Should().ThrowAsync<BindingAssertionException>().WithMessage("Timed out after *s waiting for () => vm.Count.Value > 5.");
    }
}

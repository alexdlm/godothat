namespace GodotHat.Binding.Smoke.Tests;

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AwesomeAssertions;
using GdUnit4;
using Godot;
using GodotHat.Binding.Smoke.ViewModels;
using static Mount;

[TestSuite]
[RequireGodotRuntime]
public class BindingRootTest
{
    [TestCase]
    public async Task BindsPropertiesTwoWayAndCommands()
    {
        using var errors = new ErrorLog();
        using var vm = new FormViewModel();
        var label = new Label().Bind("text", Def("Count", converter: "to_string"));
        var slider = new HSlider { MaxValue = 10, Step = 0.5 }.Bind("value", Def("Volume", BindingMode.TwoWay));
        var edit = new LineEdit().Bind("text", Def("Name", BindingMode.TwoWay));
        var option = new OptionButton().Bind("selected", Def("Speed", BindingMode.TwoWay));
        var check = new CheckBox().Bind("button_pressed", Def("Enabled", BindingMode.TwoWay));
        var increment = new Button().Bind("pressed", Command("Increment"));
        var reset = new Button().Bind("pressed", Command("Reset"));
        var root = new BindingRoot { Context = vm }.With(label, slider, edit, option, check, increment, reset);

        await AddToTree(root);

        label.Text.Should().Be("0");
        option.ItemCount.Should().Be(2, "an unpopulated OptionButton lists the enum's names");
        option.GetItemText(1).Should().Be("Fast");
        option.Selected.Should().Be(0);
        reset.Disabled.Should().BeTrue("Reset can't execute while the count is zero");

        increment.EmitSignal(BaseButton.SignalName.Pressed);
        vm.Count.Value.Should().Be(1);
        label.Text.Should().Be("1");
        reset.Disabled.Should().BeFalse();

        slider.Value = 7;
        vm.Volume.Value.Should().Be(7);
        vm.Volume.Value = 3;
        slider.Value.Should().Be(3);

        edit.Text = "Ada";
        edit.EmitSignal(LineEdit.SignalName.TextChanged, "Ada");
        vm.Name.Value.Should().Be("Ada");

        option.Select(1);
        option.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
        vm.Speed.Value.Should().Be(Speed.Fast);
        vm.Speed.Value = Speed.Slow;
        option.Selected.Should().Be(0);

        check.ButtonPressed = true;
        vm.Enabled.Value.Should().BeTrue();
        vm.Enabled.Value = false;
        check.ButtonPressed.Should().BeFalse();

        reset.EmitSignal(BaseButton.SignalName.Pressed);
        label.Text.Should().Be("0");

        errors.Errors.Should().BeEmpty();
        await Unmount(root);
    }

    [TestCase]
    public async Task WorkerThreadUpdatesApplyOnMainThread()
    {
        using var errors = new ErrorLog();
        using var vm = new FormViewModel();
        var label = new Label().Bind("text", Def("Count", converter: "to_string"));
        var root = await AddToTree(new BindingRoot { Context = vm }.With(label));

        await Task.Run(() => vm.Count.Value = 42);
        await Frames(2);

        label.Text.Should().Be("42");
        errors.Errors.Should().BeEmpty();
        await Unmount(root);
    }

    [TestCase]
    public async Task ViewModelTypeIsCreatedAndDisposedWithTheRoot()
    {
        var label = new Label().Bind("text", Def("Count", converter: "to_string"));
        var root = new BindingRoot { ViewModelType = typeof(CounterViewModel).FullName! }.With(label);

        await AddToTree(root);
        var vm = root.ViewModel.Should().BeOfType<CounterViewModel>().Subject;
        vm.Increment.Execute(R3.Unit.Default);
        label.Text.Should().Be("1");

        MainTree.Root.RemoveChild(root);
        vm.IsDisposed.Should().BeTrue();
        root.IsBound.Should().BeFalse();
        root.Free();
    }

    [TestCase]
    public async Task RootRebindsWhenReAddedToTheTree()
    {
        using var vm = new FormViewModel();
        var label = new Label().Bind("text", Def("Name"));
        var root = await AddToTree(new BindingRoot { Context = vm }.With(label));

        MainTree.Root.RemoveChild(root);
        root.IsBound.Should().BeFalse();
        vm.Name.HasObservers.Should().BeFalse();
        vm.Name.Value = "back";
        label.Text.Should().Be("");

        MainTree.Root.AddChild(root);
        root.IsBound.Should().BeTrue();
        label.Text.Should().Be("back");
        await Unmount(root);
    }

    [TestCase]
    public async Task ContextSetLaterRebinds()
    {
        var label = new Label { Text = "none" }.Bind("text", Def("Name"));
        var root = await AddToTree(new BindingRoot().With(label));
        label.Text.Should().Be("none");

        using var first = new FormViewModel();
        first.Name.Value = "first";
        root.Context = first;
        label.Text.Should().Be("first");

        using var second = new FormViewModel();
        second.Name.Value = "second";
        root.Context = second;
        label.Text.Should().Be("second");
        first.Name.HasObservers.Should().BeFalse();
        await Unmount(root);
    }

    [TestCase]
    public async Task ContextBindingScopesSubtree()
    {
        using var errors = new ErrorLog();
        using var vm = new FormViewModel();
        var name = new Label { Text = "fallback" }.Bind("text", Def("Name"));
        var panel = new PanelContainer().Bind(BindingRootBase.ContextKey, Def("Child")).Bind("visible", Def("Child", converter: "not_null"));
        var root = await AddToTree(new BindingRoot { Context = vm }.With(panel.With(name)));

        panel.Visible.Should().BeFalse();
        name.Text.Should().Be("fallback");

        var child = new FormViewModel();
        child.Name.Value = "child";
        vm.Child.Value = child;
        panel.Visible.Should().BeTrue();
        name.Text.Should().Be("child");

        vm.Child.Value = null;
        name.Text.Should().Be("fallback");
        errors.Errors.Should().BeEmpty();
        await Unmount(root);
        child.Dispose();
    }

    [TestCase]
    public async Task NodesAddedLaterAreBoundAndRemovedAreUnbound()
    {
        using var vm = new FormViewModel();
        var container = new VBoxContainer();
        var root = await AddToTree(new BindingRoot { Context = vm }.With(container));

        var label = new Label().Bind("text", Def("Count", converter: "to_string"));
        var nested = new Label().Bind("text", Def("Name"));
        container.AddChild(new MarginContainer().With(nested));
        container.AddChild(label);
        label.Text.Should().Be("0");

        vm.Name.Value = "late";
        nested.Text.Should().Be("late");

        container.RemoveChild(label);
        vm.Count.Value = 9;
        label.Text.Should().Be("0", "removed nodes are unbound");

        container.AddChild(label);
        label.Text.Should().Be("9", "re-added nodes are bound again");
        await Unmount(root);
    }

    [TestCase]
    public async Task NestedRootWithoutViewModelInheritsContext()
    {
        using var vm = new FormViewModel();
        var label = new Label().Bind("text", Def("Count", converter: "to_string"));
        var root = await AddToTree(new BindingRoot { Context = vm }.With(new BindingRoot().With(label)));

        vm.Count.Value = 3;
        label.Text.Should().Be("3");
        await Unmount(root);
    }

    [TestCase]
    public async Task NodeContextFallsBackWhenTheNodeLeaves()
    {
        foreach (bool disposeOnExit in new[] { false, true })
        {
            using var errors = new ErrorLog();
            var score = new ScoreNode { DisposeOnExit = disposeOnExit };
            var label = new Label { Text = "gone" }.Bind("text", Def("Score", converter: "to_string"));
            var root = new BindingRoot { ContextNode = score }.With(label);
            var parent = await AddToTree(new Node().With(score, root));

            label.Text.Should().Be("5");
            score.Score.Value = 6;
            label.Text.Should().Be("6");

            parent.RemoveChild(score);
            score.QueueFree();
            label.Text.Should().Be("gone", $"the node view model left the tree (disposes its members: {disposeOnExit})");
            errors.Errors.Should().BeEmpty();
            await Unmount(parent);
        }
    }

    [TestCase]
    public async Task EventBindingsCallMethods()
    {
        using var vm = new FormViewModel();
        var label = new Label().Bind("set_text", Def("Flashed", kind: BindingKind.Event));
        var root = await AddToTree(new BindingRoot { Context = vm }.With(label));

        vm.Flashed.OnNext("flash!");
        label.Text.Should().Be("flash!");
        await Unmount(root);
    }

    [TestCase]
    public async Task ErrorsAreReportedAndLeaveTheSceneValue()
    {
        using var errors = new ErrorLog();
        using var vm = new FormViewModel();
        var label = new Label { Text = "authored" }.Bind("text", Def("Missing"));
        var wrongType = new Label { Text = "authored" }.Bind("text", Def("Count"));
        var root = await AddToTree(new BindingRoot { Context = vm }.With(label, wrongType));

        label.Text.Should().Be("authored");
        wrongType.Text.Should().Be("authored");
        errors.Errors.Should().HaveCount(2);
        errors.Errors[0].Message.Should().Be("FormViewModel has no bindable member 'Missing'.");
        errors.Errors[1].Message.Should().Contain("'text' is String, but the binding gives Int32");
        await Unmount(root);
    }

    [TestCase]
    public async Task MetadataRoundTripsThroughSavedScenes()
    {
        var label = new Label { Name = "Title" }
            .Bind("text", Def("Count", BindingMode.OneTime, "format", "Count: {0}"))
            .Bind("visible", new BindingDef { Path = "Enabled", Source = BindingSourceKind.ParentContext, Fallback = true, Coalesce = true });
        var root = new BindingRoot { Name = "Root", ViewModelType = typeof(FormViewModel).FullName! }.With(label);
        label.Owner = root;

        var packed = new PackedScene();
        packed.Pack(root).Should().Be(Error.Ok);
        const string path = "user://binding_round_trip.tscn";
        ResourceSaver.Save(packed, path).Should().Be(Error.Ok);
        root.Free();

        string text = FileAccess.GetFileAsString(path);
        text.Should().Contain("metadata/godothat_bindings");
        var loaded = ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BindingRoot>();

        loaded.ViewModelType.Should().Be(typeof(FormViewModel).FullName);
        Godot.Collections.Dictionary bindings = loaded.GetNode("Title").GetMeta(BindingRootBase.BindingsMetaKey).AsGodotDictionary();
        var textBinding = bindings["text"].As<BindingDef>();
        (textBinding.Path, textBinding.Mode, textBinding.Converter.ToString(), textBinding.ConverterArg)
            .Should().Be(("Count", BindingMode.OneTime, "format", "Count: {0}"));
        var visibleBinding = bindings["visible"].As<BindingDef>();
        (visibleBinding.Source, visibleBinding.Fallback.AsBool(), visibleBinding.Coalesce)
            .Should().Be((BindingSourceKind.ParentContext, true, true));
        loaded.Free();
    }

    [TestCase]
    public async Task FreedScenesAndViewModelsAreCollected()
    {
        (WeakReference viewModel, WeakReference label) = await MountAndFree();
        await Frames(3);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        viewModel.IsAlive.Should().BeFalse("nothing in the binding layer holds the view model");
        label.IsAlive.Should().BeFalse("nothing in the binding layer holds the Controls");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference, WeakReference)> MountAndFree()
    {
        var vm = new FormViewModel();
        var label = new Label().Bind("text", Def("Count", converter: "to_string"));
        var slider = new HSlider().Bind("value", Def("Volume", BindingMode.TwoWay));
        var button = new Button().Bind("pressed", Command("Increment"));
        var root = new BindingRoot { Context = vm }.With(label, slider, button);
        await AddToTree(root);
        vm.Count.Value = 1;

        MainTree.Root.RemoveChild(root);
        await Unmount(root);
        return (new WeakReference(vm), new WeakReference(label));
    }
}

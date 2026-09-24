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
public class ItemsTest
{
    // A row: a button showing the item's name that removes it through the page's command.
    private static PackedScene RowTemplate()
    {
        var row = new Button { Name = "Row" }
            .Bind("text", Def("Name"))
            .Bind("pressed", new BindingDef { Kind = BindingKind.Command, Path = "Remove", Source = BindingSourceKind.ParentContext, Parameter = BindingParameterKind.Item });
        var scene = new PackedScene();
        scene.Pack(row);
        row.Free();
        return scene;
    }

    private static PackedScene TagTemplate()
    {
        var row = new Label().Bind("text", Def("Label"));
        var scene = new PackedScene();
        scene.Pack(row);
        row.Free();
        return scene;
    }

    private static string[] Texts(Node container, int skip = 0) =>
        container.GetChildren().Skip(skip).Select(child => ((Button)child).Text).ToArray();

    [TestCase]
    public async Task ContainerFollowsCollectionIncrementally()
    {
        using var errors = new ErrorLog();
        using var vm = new TodoListViewModel();
        vm.Items.AddRange([new TodoItem("a"), new TodoItem("b")]);
        var header = new Label { Name = "Header" };
        var list = new VBoxContainer().With(header).Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items", Template = RowTemplate() });
        var count = new Label().Bind("text", Def("Items", converter: "format", argument: "{0} items"));
        var root = await AddToTree(new BindingRoot { Context = vm }.With(list, count));

        list.GetChild(0).Should().BeSameAs(header, "rows go after the container's own children");
        Texts(list, 1).Should().Equal("a", "b");
        count.Text.Should().Be("2 items");

        vm.Items.Add(new TodoItem("c"));
        vm.Items.Insert(0, new TodoItem("z"));
        vm.Items.Move(0, 3);
        vm.Items[1].Name.Value = "B";
        Texts(list, 1).Should().Equal("a", "B", "c", "z");

        vm.Items.RemoveAt(3);
        vm.Items.Sort(Comparer<TodoItem>.Create((x, y) => string.CompareOrdinal(y.Name.Value, x.Name.Value)));
        Texts(list, 1).Should().Equal("c", "a", "B");
        count.Text.Should().Be("3 items");

        vm.Items.Clear();
        list.GetChildCount().Should().Be(1);
        count.Text.Should().Be("0 items");
        errors.Errors.Should().BeEmpty();
        await Unmount(root);
    }

    [TestCase]
    public async Task RowCommandsReceiveTheirItemAndFocusSurvivesRemoves()
    {
        using var errors = new ErrorLog();
        using var vm = new TodoListViewModel();
        var items = Enumerable.Range(0, 5).Select(i => new TodoItem($"item {i}")).ToArray();
        vm.Items.AddRange(items);
        var list = new VBoxContainer().Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items", Template = RowTemplate() });
        var root = await AddToTree(new BindingRoot { Context = vm }.With(list));

        var middle = (Button)list.GetChild(2);
        middle.GrabFocus();
        ((Button)list.GetChild(4)).EmitSignal(BaseButton.SignalName.Pressed);
        ((Button)list.GetChild(0)).EmitSignal(BaseButton.SignalName.Pressed);

        vm.Items.Should().Equal(items[1], items[2], items[3]);
        Texts(list).Should().Equal("item 1", "item 2", "item 3");
        list.GetChild(1).Should().BeSameAs(middle, "rows for remaining items are kept, not rebuilt");
        middle.HasFocus().Should().BeTrue();

        vm.Items.Add(new TodoItem("new"));
        Texts(list).Should().Equal("item 1", "item 2", "item 3", "new");
        middle.HasFocus().Should().BeTrue();
        errors.Errors.Should().BeEmpty();
        // The removed rows wait in the pool, detached, until reused or freed, which GdUnit reports as orphans
        await Unmount(root);
        Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount).Should().Be(0);
    }

    [TestCase]
    public async Task RemovedRowsArePooledAndReused()
    {
        using var vm = new TodoListViewModel();
        vm.Items.Add(new TodoItem("first"));
        var list = new VBoxContainer().Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items", Template = RowTemplate() });
        var root = await AddToTree(new BindingRoot { Context = vm }.With(list));
        Node row = list.GetChild(0);

        vm.Items.RemoveAt(0);
        list.GetChildCount().Should().Be(0);
        vm.Items.Add(new TodoItem("second"));

        list.GetChild(0).Should().BeSameAs(row);
        ((Button)row).Text.Should().Be("second");
        await Unmount(root);
    }

    [TestCase]
    public async Task ReplacedRecordsUpdateTheirRow()
    {
        using var vm = new TodoListViewModel();
        vm.Tags.AddRange([new Tag("red", 1), new Tag("blue", 2)]);
        var list = new HBoxContainer().Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Tags", Template = TagTemplate() });
        var root = await AddToTree(new BindingRoot { Context = vm }.With(list));
        Node blue = list.GetChild(1);

        vm.Tags[1] = vm.Tags[1] with { Label = "navy" };

        list.GetChild(1).Should().BeSameAs(blue);
        ((Label)blue).Text.Should().Be("navy");
        await Unmount(root);
    }

    [TestCase]
    public async Task WorkerThreadChangesApplyOnMainThread()
    {
        using var vm = new TodoListViewModel();
        var list = new VBoxContainer().Bind(BindingRootBase.ItemsKey, new BindingDef { Kind = BindingKind.Items, Path = "Items", Template = RowTemplate() });
        var root = await AddToTree(new BindingRoot { Context = vm }.With(list));

        await Task.Run(() => vm.Items.AddRange([new TodoItem("x"), new TodoItem("y")]));
        await Frames(2);

        Texts(list).Should().Equal("x", "y");
        await Unmount(root);
    }

    [TestCase]
    public async Task ListControlsShowItemsAndBindSelection()
    {
        using var errors = new ErrorLog();
        using var vm = new TodoListViewModel();
        var a = new TodoItem("a");
        var b = new TodoItem("b");
        vm.Items.AddRange([a, b]);
        var items = new BindingDef { Kind = BindingKind.Items, Path = "Items", ItemText = "Name" };
        var selected = Def("Selected", BindingMode.TwoWay);
        var itemList = new ItemList().Bind(BindingRootBase.ItemsKey, items).Bind(BindingRootBase.SelectedKey, selected);
        var option = new OptionButton().Bind(BindingRootBase.ItemsKey, items).Bind(BindingRootBase.SelectedKey, selected);
        var tree = new Tree().Bind(BindingRootBase.ItemsKey, items);
        var root = await AddToTree(new BindingRoot { Context = vm }.With(itemList, option, tree));

        itemList.ItemCount.Should().Be(2);
        itemList.GetItemText(1).Should().Be("b");
        option.GetItemText(0).Should().Be("a");
        tree.GetRoot().GetChildCount().Should().Be(2);

        vm.Items.Insert(0, new TodoItem("z"));
        vm.Items.Move(0, 2);
        a.Name.Value = "A";
        string[] expected = ["A", "b", "z"];
        Enumerable.Range(0, 3).Select(itemList.GetItemText).Should().Equal(expected);
        Enumerable.Range(0, 3).Select(option.GetItemText).Should().Equal(expected);
        tree.GetRoot().GetChildren().Select(item => item.GetText(0)).Should().Equal(expected);

        vm.Selected.Value = b;
        itemList.IsSelected(1).Should().BeTrue();
        option.Selected.Should().Be(1);

        itemList.Select(0);
        itemList.EmitSignal(ItemList.SignalName.ItemSelected, 0);
        vm.Selected.Value.Should().BeSameAs(a);
        option.Selected.Should().Be(0);

        vm.Items.Remove(a);
        itemList.ItemCount.Should().Be(2);
        option.ItemCount.Should().Be(2);
        errors.Errors.Should().BeEmpty();
        await Unmount(root);
    }
}

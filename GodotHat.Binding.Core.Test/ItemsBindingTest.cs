using AwesomeAssertions;
using CsCheck;
using GodotHat.Binding.Core.Test.Support;
using ObservableCollections;
using R3;

namespace GodotHat.Binding.Core.Test;

public class ItemsBindingTest
{
    private readonly ErrorCollector errors = new();
    private readonly BindingEngine engine;

    public ItemsBindingTest()
    {
        this.engine = new BindingEngine(BindingDispatcher.Immediate, this.errors);
    }

    private FakeItemsTarget Bind(object vm, string path)
    {
        var target = new FakeItemsTarget();
        this.engine.BindItems(DataContext.Constant(vm), new BindingSpec { Kind = BindingKind.Items, Path = path }, target);
        return target;
    }

    [Fact]
    public void ListChangesApplyIncrementally()
    {
        var vm = new InventoryViewModel();
        vm.Items.AddRange(["a", "b"]);
        FakeItemsTarget target = this.Bind(vm, "Items");

        vm.Items.Add("c");
        vm.Items.Insert(0, "z");
        vm.Items.AddRange(["d", "e"]);
        vm.Items.RemoveAt(1);
        vm.Items.RemoveRange(2, 2);
        vm.Items.Move(0, 2);
        vm.Items[0] = "B";

        target.Items.Should().Equal(vm.Items);
        target.Log.Should().Equal(
            "reset 2",
            "insert 2 1",
            "insert 0 1",
            "insert 4 2",
            "remove 1 1",
            "remove 2 2",
            "move 0 2",
            "replace 0");
        this.errors.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ClearSortAndReverseReset()
    {
        var vm = new InventoryViewModel();
        vm.Items.AddRange(["c", "a", "b"]);
        FakeItemsTarget target = this.Bind(vm, "Items");

        vm.Items.Sort();
        target.Items.Should().Equal("a", "b", "c");
        vm.Items.Reverse();
        target.Items.Should().Equal("c", "b", "a");
        vm.Items.Clear();
        target.Items.Should().BeEmpty();
        target.Log.Should().Equal("reset 3", "reset 3", "reset 3", "reset 0");
    }

    [Fact]
    public void UnorderedCollectionsAppendAndRemoveByValue()
    {
        var vm = new InventoryViewModel();
        vm.Tags.Add(1);
        FakeItemsTarget tags = this.Bind(vm, "Tags");
        FakeItemsTarget stock = this.Bind(vm, "Stock");

        vm.Tags.AddRange([2, 3, 1]);
        vm.Tags.AddRange([1]);
        vm.Tags.Remove(2);
        tags.Items.Should().Equal(1, 3);
        tags.Log.Should().Equal("reset 1", "insert 1 2", "remove 1 1");

        vm.Stock.Add("apple", 1);
        vm.Stock.Add("pear", 2);
        vm.Stock["apple"] = 5;
        vm.Stock.Remove("pear");
        stock.Items.Should().Equal(new KeyValuePair<string, int>("apple", 5));
    }

    [Fact]
    public void QueuesStacksAndRingBuffersKeepEnumerationOrder()
    {
        var vm = new InventoryViewModel();
        FakeItemsTarget queue = this.Bind(vm, "Queue");
        FakeItemsTarget stack = this.Bind(vm, "Stack");
        FakeItemsTarget recent = this.Bind(vm, "Recent");

        vm.Queue.EnqueueRange([1, 2, 3]);
        vm.Queue.Dequeue();
        vm.Stack.Push(1);
        vm.Stack.PushRange([2, 3]);
        vm.Stack.Pop();
        for (int i = 1; i <= 5; i++)
        {
            vm.Recent.AddLast(i);
        }

        vm.Recent.AddLastRange([6, 7]);

        queue.Items.Should().Equal(vm.Queue.Cast<object>());
        stack.Items.Should().Equal(vm.Stack.Cast<object>());
        recent.Items.Should().Equal(vm.Recent.Cast<object>());
    }

    [Fact]
    public void ViewsResetToTheirVisibleItems()
    {
        var vm = new InventoryViewModel();
        vm.Items.AddRange(["apple", "pear"]);
        FakeItemsTarget target = this.Bind(vm, "Shouted");
        target.Items.Should().Equal("APPLE", "PEAR");

        vm.Items.Add("plum");
        target.Items.Should().Equal("APPLE", "PEAR", "PLUM");

        vm.Shouted.AttachFilter(item => item.StartsWith('p'));
        target.Items.Should().Equal("PEAR", "PLUM");

        vm.Items.Insert(0, "peach");
        vm.Items.Add("banana");
        target.Items.Should().Equal("PEACH", "PEAR", "PLUM");

        vm.Shouted.ResetFilter();
        target.Items.Should().Equal("PEACH", "APPLE", "PEAR", "PLUM", "BANANA");
    }

    [Fact]
    public void FollowsOwnerChangesAlongThePath()
    {
        var vm = new InventoryViewModel();
        FakeItemsTarget target = this.Bind(vm, "Other.Items");
        target.Items.Should().BeEmpty();

        var first = new InventoryViewModel();
        first.Items.AddRange(["a", "b"]);
        vm.Other.Value = first;
        target.Items.Should().Equal("a", "b");

        var second = new InventoryViewModel();
        second.Items.Add("x");
        vm.Other.Value = second;
        target.Items.Should().Equal("x");

        first.Items.Add("ignored");
        target.Items.Should().Equal("x");

        vm.Other.Value = null;
        target.Items.Should().BeEmpty();
    }

    [Fact]
    public void CollectionsBoundToPropertiesBindTheirCount()
    {
        var vm = new InventoryViewModel();
        var count = new FakeTarget();
        var any = new FakeTarget();
        var text = new FakeTarget();
        this.engine.BindProperty(DataContext.Constant(vm), new BindingSpec { Path = "Items" }, count);
        this.engine.BindProperty(DataContext.Constant(vm), new BindingSpec { Path = "Items", Converter = BuiltInConverters.AnyId }, any);
        this.engine.BindProperty(DataContext.Constant(vm), new BindingSpec { Path = "Shouted", Converter = BuiltInConverters.FormatId, ConverterArgument = "{0} items" }, text);

        count.Value.Should().Be(0);
        any.Value.Should().Be(false);

        vm.Items.AddRange(["a", "b"]);
        count.Value.Should().Be(2);
        any.Value.Should().Be(true);
        text.Value.Should().Be("2 items");

        vm.Items[0] = "c";
        count.Applied.Should().Equal(0, 2);
    }

    [Fact]
    public void AnyRejectsNonCollections()
    {
        var target = new FakeTarget();
        this.engine.BindProperty(DataContext.Constant(new PersonViewModel()), new BindingSpec { Path = "Name", Converter = BuiltInConverters.AnyId }, target);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Be("'any' needs a collection or count, not String.");
    }

    [Fact]
    public void NonCollectionsReport()
    {
        FakeItemsTarget target = this.Bind(new PersonViewModel(), "Name");
        target.Log.Should().Equal("reset 0");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Be("Name is not a collection.");
    }

    [Fact]
    public void DisposingStopsUpdates()
    {
        var vm = new InventoryViewModel();
        var target = new FakeItemsTarget();
        IDisposable binding = this.engine.BindItems(DataContext.Constant(vm), new BindingSpec { Path = "Items" }, target);

        binding.Dispose();
        vm.Items.Add("late");

        target.Log.Should().Equal("reset 0");
    }

    [Fact]
    public void WorkerThreadChangesApplyInOrderOnMainThread()
    {
        var frame = new ManualSynchronizationContext();
        var engine = new BindingEngine(new BindingDispatcher(frame, Environment.CurrentManagedThreadId), this.errors);
        var vm = new InventoryViewModel();
        var target = new FakeItemsTarget();
        using IDisposable binding = engine.BindItems(DataContext.Constant(vm), new BindingSpec { Path = "Items" }, target);

        var worker = new Thread(() =>
        {
            vm.Items.AddRange(["a", "b", "c"]);
            vm.Items.RemoveAt(1);
            vm.Items[0] = "A";
        });
        worker.Start();
        worker.Join();
        vm.Items.Add("main");
        target.Items.Should().BeEmpty("changes queue behind the worker's until the frame runs");

        frame.RunFrame();

        target.Items.Should().Equal("A", "c", "main");
        target.ThreadId.Should().Be(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void RandomListOperationsKeepTargetInStep()
    {
        Gen<Action<ObservableList<int>>> operation = Gen.OneOf(
            Gen.Int[0, 99].Select(x => (Action<ObservableList<int>>)(l => l.Add(x))),
            Gen.Int[0, 99].Array[0, 4].Select(xs => (Action<ObservableList<int>>)(l => l.AddRange(xs))),
            Gen.Select(Gen.Int[0, 1000], Gen.Int[0, 99], (i, x) => (Action<ObservableList<int>>)(l => l.Insert(i % (l.Count + 1), x))),
            Gen.Int[0, 1000].Select(i => (Action<ObservableList<int>>)(l =>
            {
                if (l.Count > 0)
                {
                    l.RemoveAt(i % l.Count);
                }
            })),
            Gen.Select(Gen.Int[0, 1000], Gen.Int[0, 5], (i, n) => (Action<ObservableList<int>>)(l =>
            {
                if (l.Count > 0)
                {
                    int index = i % l.Count;
                    l.RemoveRange(index, Math.Min(n, l.Count - index));
                }
            })),
            Gen.Select(Gen.Int[0, 1000], Gen.Int[0, 1000], (a, b) => (Action<ObservableList<int>>)(l =>
            {
                if (l.Count > 0)
                {
                    l.Move(a % l.Count, b % l.Count);
                }
            })),
            Gen.Select(Gen.Int[0, 1000], Gen.Int[0, 99], (i, x) => (Action<ObservableList<int>>)(l =>
            {
                if (l.Count > 0)
                {
                    l[i % l.Count] = x;
                }
            })),
            Gen.Const((Action<ObservableList<int>>)(l => l.Sort())),
            Gen.Const((Action<ObservableList<int>>)(l => l.Reverse())),
            Gen.Const((Action<ObservableList<int>>)(l => l.Clear())));

        operation.Array[0, 40].Sample(operations =>
        {
            var list = new ObservableList<int>();
            var target = new FakeItemsTarget();
            var subscriptionEngine = new BindingEngine(BindingDispatcher.Immediate, new ErrorCollector());
            var owner = new ListOwner(list);
            using IDisposable binding = subscriptionEngine.BindItems(DataContext.Constant(owner), new BindingSpec { Path = "List" }, target);

            foreach (Action<ObservableList<int>> apply in operations)
            {
                apply(list);
                if (!target.Items.Cast<int>().SequenceEqual(list))
                {
                    return false;
                }
            }

            return true;
        });
    }

    // A minimal owner for the property-based test, registered on first use.
    public sealed class ListOwner(ObservableList<int> list)
    {
        static ListOwner()
        {
            BindingRegistry.Register(new ViewModelAccessor(
                typeof(ListOwner),
                [new CollectionItemsMember<ListOwner, int>("List", static o => o.List)]));
        }

        public ObservableList<int> List { get; } = list;
    }
}

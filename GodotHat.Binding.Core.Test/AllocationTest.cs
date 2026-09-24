using AwesomeAssertions;
using GodotHat.Binding.Core.Test.Support;

namespace GodotHat.Binding.Core.Test;

// Updates flow from source to target with concrete types, so a steady-state update allocates nothing.
public class AllocationTest
{
    private readonly ErrorCollector errors = new();

    private static long MeasureUpdates(Action<int> update)
    {
        for (int i = 0; i < 1000; i++)
        {
            update(i);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1000; i < 2000; i++)
        {
            update(i);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void OneWayUpdateDoesNotAllocate()
    {
        var vm = new PersonViewModel();
        var target = new TypedTarget<int>();
        var engine = new BindingEngine(BindingDispatcher.Immediate, this.errors);
        using IDisposable binding = engine.BindProperty(DataContext.Constant(vm), new BindingSpec { Path = "Age" }, target);

        MeasureUpdates(i => vm.Age.Value = i).Should().Be(0);
        target.Value.Should().Be(1999);
    }

    [Fact]
    public void UpdateThroughPathAndConverterDoesNotAllocate()
    {
        var vm = new PersonViewModel();
        vm.Friend.Value = new PersonViewModel();
        var target = new TypedTarget<bool>();
        var engine = new BindingEngine(
            new BindingDispatcher(new ManualSynchronizationContext(), Environment.CurrentManagedThreadId),
            this.errors);
        using IDisposable binding = engine.BindProperty(
            DataContext.Constant(vm),
            new BindingSpec { Path = "Friend.Age", Converter = BuiltInConverters.EqualsId, ConverterArgument = "7" },
            target);

        MeasureUpdates(i => vm.Friend.Value!.Age.Value = i % 10).Should().Be(0);
        target.Value.Should().BeFalse();
        this.errors.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CoalescedUpdatesDoNotAllocate()
    {
        var frame = new ManualSynchronizationContext();
        var vm = new PersonViewModel();
        var target = new TypedTarget<int>();
        var engine = new BindingEngine(new BindingDispatcher(frame, Environment.CurrentManagedThreadId), this.errors);
        using IDisposable binding = engine.BindProperty(
            DataContext.Constant(vm),
            new BindingSpec { Path = "Age", Coalesce = true },
            target);

        MeasureUpdates(i =>
        {
            vm.Age.Value = i;
            if (i % 3 == 0)
            {
                frame.RunFrame();
            }
        }).Should().Be(0);
    }
}

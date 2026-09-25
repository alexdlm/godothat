using AwesomeAssertions;
using GodotHat.Binding.Core.Test.Support;
using R3;

namespace GodotHat.Binding.Core.Test;

// Tests join worker threads rather than awaiting tasks, so they keep running on the thread the dispatcher treats as
// main.
public class ThreadingTest
{
    private readonly ErrorCollector errors = new();
    private readonly ManualSynchronizationContext frame = new();
    private readonly BindingEngine engine;

    public ThreadingTest()
    {
        this.engine = new BindingEngine(
            new BindingDispatcher(this.frame, Environment.CurrentManagedThreadId),
            this.errors);
    }

    private static void OnWorker(Action action)
    {
        var thread = new Thread(() => action());
        thread.Start();
        thread.Join();
    }

    private (PersonViewModel Vm, FakeTarget Target, IDisposable Binding) BindAge(bool coalesce = false)
    {
        var vm = new PersonViewModel();
        var target = new FakeTarget();
        IDisposable binding = this.engine.BindProperty(
            DataContext.Constant(vm),
            new BindingSpec { Path = "Age", Coalesce = coalesce },
            target);
        return (vm, target, binding);
    }

    [Fact]
    public void MainThreadUpdatesApplyImmediately()
    {
        var (vm, target, _) = this.BindAge();

        vm.Age.Value = 3;

        target.Value.Should().Be(3);
        this.frame.Pending.Should().Be(0);
    }

    [Fact]
    public void WorkerThreadUpdatesApplyOnNextFrameOnMainThread()
    {
        var (vm, target, _) = this.BindAge();

        OnWorker(() => vm.Age.Value = 5);
        target.Value.Should().Be(0, "nothing is applied until the frame runs");

        this.frame.RunFrame();
        target.Value.Should().Be(5);
        target.ThreadId.Should().Be(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void QueuedUpdatesKeepOrderWithLaterMainThreadUpdates()
    {
        var (vm, target, _) = this.BindAge();

        OnWorker(() => vm.Age.Value = 1);
        vm.Age.Value = 2;
        target.Applied.Should().Equal([0], "the main thread update queues behind the worker's");

        this.frame.RunFrame();
        target.Applied.Should().Equal(0, 1, 2);

        vm.Age.Value = 3;
        target.Applied.Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void CoalescedUpdatesApplyLastValueOncePerFrame()
    {
        var (vm, target, _) = this.BindAge(coalesce: true);
        this.frame.RunFrame();
        target.Applied.Should().Equal(0);

        vm.Age.Value = 1;
        vm.Age.Value = 2;
        vm.Age.Value = 3;
        target.Applied.Should().Equal(0);

        this.frame.RunFrame();
        target.Applied.Should().Equal(0, 3);
        this.frame.Pending.Should().Be(0);
    }

    [Fact]
    public void CoalescedWorkerUpdates()
    {
        var (vm, target, _) = this.BindAge(coalesce: true);
        this.frame.RunFrame();

        OnWorker(() =>
        {
            for (int i = 1; i <= 100; i++)
            {
                vm.Age.Value = i;
            }
        });
        this.frame.RunFrame();

        target.Applied.Should().Equal(0, 100);
    }

    [Fact]
    public void CoalescedValueStaysAheadOfLaterError()
    {
        var source = new Subject<int>();
        var delivered = new List<string>();
        var dispatcher = new BindingDispatcher(this.frame, Environment.CurrentManagedThreadId);
        using IDisposable subscription = source.ObserveOnMainThread(dispatcher, coalesce: true)
            .Subscribe(v => delivered.Add($"value {v}"), e => delivered.Add($"error {e.Message}"), _ => { });

        source.OnNext(1);
        source.OnNext(2);
        source.OnErrorResume(new InvalidOperationException("boom"));
        source.OnNext(3);
        this.frame.RunFrame();

        delivered.Should().Equal("value 2", "error boom", "value 3");
    }

    [Fact]
    public void DisposedBeforeFrameDropsQueuedUpdates()
    {
        var (vm, target, binding) = this.BindAge();

        OnWorker(() => vm.Age.Value = 9);
        binding.Dispose();
        this.frame.RunFrame();

        target.Applied.Should().Equal(0);
    }

    [Fact]
    public void CommandAvailabilityIsMarshalled()
    {
        var vm = new PersonViewModel();
        var target = new FakeCommandTarget();
        using IDisposable binding = this.engine.BindCommand(DataContext.Constant(vm), new BindingSpec { Path = "AddYears" }, target);
        target.Available.Should().BeTrue();

        OnWorker(() => vm.CanAddYears.Value = false);
        target.Available.Should().BeTrue();

        this.frame.RunFrame();
        target.Available.Should().BeFalse();
    }

    [Fact]
    public void EventsFromWorkersAreAllDelivered()
    {
        var vm = new PersonViewModel();
        var target = new FakeEventTarget();
        using IDisposable binding = this.engine.BindEvent(DataContext.Constant(vm), new BindingSpec { Path = "Messages" }, target);

        OnWorker(() =>
        {
            vm.Messages.OnNext("a");
            vm.Messages.OnNext("b");
        });
        this.frame.RunFrame();

        target.Calls.Should().Equal("a", "b");
    }

    [Fact]
    public void ContextChangesFromWorkersApplyOnMainThread()
    {
        var team = new TeamViewModel();
        DataContext leader = this.engine.BindContext(DataContext.Constant(team), new BindingSpec { Path = "Leader" }, "panel", out _);
        var name = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(leader, new BindingSpec { Path = "Name" }, name);

        OnWorker(() => team.Leader.Value = new PersonViewModel("Worker"));
        name.Value.Should().Be("<fallback>");

        this.frame.RunFrame();
        name.Value.Should().Be("Worker");
        name.ThreadId.Should().Be(Environment.CurrentManagedThreadId);
    }
}

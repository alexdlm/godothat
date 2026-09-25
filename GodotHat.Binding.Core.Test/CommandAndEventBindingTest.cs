using AwesomeAssertions;
using GodotHat.Binding.Core.Test.Support;
using R3;

namespace GodotHat.Binding.Core.Test;

public class CommandAndEventBindingTest
{
    private readonly ErrorCollector errors = new();
    private readonly BindingEngine engine;

    public CommandAndEventBindingTest()
    {
        this.engine = new BindingEngine(BindingDispatcher.Immediate, this.errors);
    }

    [Fact]
    public void ExecutesCommandWithoutParameter()
    {
        var vm = new PersonViewModel();
        int greeted = 0;
        vm.Greet.Subscribe(_ => greeted++);
        var target = new FakeCommandTarget();

        using IDisposable binding = this.engine.BindCommand(DataContext.Constant(vm), new BindingSpec { Path = "Greet" }, target);
        target.Available.Should().BeTrue();

        target.Press();
        target.Press();
        greeted.Should().Be(2);
    }

    [Fact]
    public void AvailabilityFollowsCanExecuteAndGuardsExecute()
    {
        var vm = new PersonViewModel();
        var target = new FakeCommandTarget();
        using IDisposable binding = this.engine.BindCommand(
            DataContext.Constant(vm),
            new BindingSpec { Path = "AddYears", Parameter = BindingParameterKind.Constant, ParameterValue = "2" },
            target);

        target.Press();
        vm.Added.Should().Equal(2);

        vm.CanAddYears.Value = false;
        target.Available.Should().BeFalse();
        target.Press();
        vm.Added.Should().Equal([2], "the command can't execute");

        vm.CanAddYears.Value = true;
        target.Available.Should().BeTrue();
    }

    [Fact]
    public void ConstantParameterMustParse()
    {
        var target = new FakeCommandTarget();
        using IDisposable binding = this.engine.BindCommand(
            DataContext.Constant(new PersonViewModel()),
            new BindingSpec { Path = "AddYears", Parameter = BindingParameterKind.Constant, ParameterValue = "two" },
            target);

        target.Available.Should().BeFalse();
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("is not a Int32");
    }

    [Fact]
    public void ItemParameterPassesTheTargetsContext()
    {
        var team = new TeamViewModel();
        var greeted = new List<object?>();
        team.Captain.Greet.Subscribe(u => greeted.Add(u));
        var itemContext = DataContext.ForItem(new PersonViewModel("row"), DataContext.Constant(team));
        var target = new FakeCommandTarget();

        using IDisposable binding = this.engine.BindCommand(
            itemContext,
            new BindingSpec { Path = "Captain.Greet", Source = BindingSourceKind.ParentContext, Parameter = BindingParameterKind.Item },
            target);
        target.Press();

        greeted.Should().BeEmpty("a PersonViewModel item isn't a Unit parameter");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("is not a Unit");
    }

    [Fact]
    public void ItemParameterOfMatchingType()
    {
        var team = new TeamViewModel();
        var row = new PersonViewModel("row");
        var target = new FakeCommandTarget();

        using IDisposable binding = this.engine.BindCommand(
            DataContext.ForItem(row, DataContext.Constant(team)),
            new BindingSpec { Path = "Promote", Source = BindingSourceKind.ParentContext, Parameter = BindingParameterKind.Item },
            target);
        target.Press();

        team.Leader.Value.Should().BeSameAs(row);
    }

    [Fact]
    public void CommandFollowsOwnerChanges()
    {
        var vm = new PersonViewModel();
        var bob = new PersonViewModel();
        var target = new FakeCommandTarget();
        using IDisposable binding = this.engine.BindCommand(
            DataContext.Constant(vm),
            new BindingSpec { Path = "Friend.AddYears", Parameter = BindingParameterKind.Constant, ParameterValue = "1" },
            target);
        target.Available.Should().BeFalse("there's no friend");
        target.Press();

        vm.Friend.Value = bob;
        target.Available.Should().BeTrue();
        target.Press();
        bob.Added.Should().Equal(1);

        bob.CanAddYears.Value = false;
        target.Available.Should().BeFalse();
    }

    [Fact]
    public void DisposingCommandBindingUnsubscribes()
    {
        var vm = new PersonViewModel();
        var target = new FakeCommandTarget();
        IDisposable binding = this.engine.BindCommand(DataContext.Constant(vm), new BindingSpec { Path = "AddYears" }, target);
        vm.CanAddYears.HasObservers.Should().BeTrue();

        binding.Dispose();

        target.IsObserved.Should().BeFalse();
        vm.CanAddYears.Value = false;
        target.Available.Should().BeTrue("availability is no longer observed");
    }

    [Fact]
    public void NonCommandReports()
    {
        var target = new FakeCommandTarget();
        using IDisposable binding = this.engine.BindCommand(DataContext.Constant(new PersonViewModel()), new BindingSpec { Path = "Name" }, target);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Be("Name is not a command.");
    }

    [Fact]
    public void EventsReachTargetOncePerEmission()
    {
        var vm = new PersonViewModel();
        var target = new FakeEventTarget();
        using IDisposable binding = this.engine.BindEvent(DataContext.Constant(vm), new BindingSpec { Path = "Messages" }, target);

        vm.Messages.OnNext("a");
        vm.Messages.OnNext("a");
        vm.Messages.OnNext("b");

        target.Calls.Should().Equal("a", "a", "b");
    }

    [Fact]
    public void EventsApplyConverters()
    {
        var vm = new PersonViewModel();
        var target = new FakeEventTarget();
        using IDisposable binding = this.engine.BindEvent(
            DataContext.Constant(vm),
            new BindingSpec { Path = "Messages", Converter = BuiltInConverters.FormatId, ConverterArgument = "<{0}>" },
            target);

        vm.Messages.OnNext("hi");
        target.Calls.Should().Equal("<hi>");
    }

    [Fact]
    public void DisposingEventBindingStopsCalls()
    {
        var vm = new PersonViewModel();
        var target = new FakeEventTarget();
        IDisposable binding = this.engine.BindEvent(DataContext.Constant(vm), new BindingSpec { Path = "Messages" }, target);

        binding.Dispose();
        vm.Messages.OnNext("late");

        target.Calls.Should().BeEmpty();
    }

    [Fact]
    public void ContextBindingScopesChildren()
    {
        var team = new TeamViewModel();
        DataContext teamContext = DataContext.Constant(team);
        DataContext leader = this.engine.BindContext(teamContext, new BindingSpec { Path = "Leader" }, "panel", out IDisposable subscription);
        var name = new FakeTarget();
        var captain = new FakeTarget();
        using IDisposable nameBinding = this.engine.BindProperty(leader, new BindingSpec { Path = "Name" }, name);
        using IDisposable captainBinding = this.engine.BindProperty(
            leader,
            new BindingSpec { Path = "Captain.Name", Source = BindingSourceKind.ParentContext },
            captain);

        leader.Type.Should().Be(typeof(PersonViewModel));
        leader.Parent.Should().BeSameAs(teamContext);
        name.Value.Should().Be("<fallback>");
        captain.Value.Should().Be("Cap");

        team.Leader.Value = new PersonViewModel("Boss");
        name.Value.Should().Be("Boss");

        subscription.Dispose();
        team.Leader.HasObservers.Should().BeFalse();
        this.errors.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ContextBindingToValueTypeReports()
    {
        this.engine.BindContext(DataContext.Constant(new PersonViewModel()), new BindingSpec { Path = "Age" }, "panel", out _);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("can't be a data context");
    }
}

using AwesomeAssertions;
using GodotHat.Binding.Core.Test.Support;
using R3;

namespace GodotHat.Binding.Core.Test;

public class PropertyBindingTest
{
    private readonly ErrorCollector errors = new();
    private readonly BindingEngine engine;

    public PropertyBindingTest()
    {
        this.engine = new BindingEngine(BindingDispatcher.Immediate, this.errors);
    }

    private static DataContext ContextOf(object vm) => DataContext.Constant(vm);

    private static BindingSpec Spec(string path, BindingMode mode = BindingMode.OneWay, string? converter = null, string? arg = null) =>
        new() { Path = path, Mode = mode, Converter = converter, ConverterArgument = arg };

    [Fact]
    public void OneWayAppliesCurrentValueAndChanges()
    {
        var vm = new PersonViewModel("Ada", 36);
        var target = new FakeTarget();

        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Name"), target);
        target.Value.Should().Be("Ada");

        vm.Name.Value = "Grace";
        target.Value.Should().Be("Grace");
        target.Applied.Should().Equal("Ada", "Grace");
        this.errors.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ReadOnlyPropertyBinds()
    {
        var vm = new PersonViewModel(age: 10);
        var target = new FakeTarget();

        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("IsAdult"), target);
        target.Value.Should().Be(false);

        vm.Age.Value = 20;
        target.Value.Should().Be(true);
    }

    [Fact]
    public void ConstantIsReadOnce()
    {
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(new PersonViewModel(id: "abc")), Spec("Id"), target);
        target.Value.Should().Be("abc");
    }

    [Fact]
    public void RepeatedEqualValuesAreNotReapplied()
    {
        var vm = new PersonViewModel();
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Age"), target);

        vm.Age.OnNext(1);
        vm.Age.OnNext(1);
        vm.Age.OnNext(2);

        target.Applied.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void DottedPathFollowsIntermediateChanges()
    {
        var vm = new PersonViewModel("Ada");
        var bob = new PersonViewModel("Bob");
        var cat = new PersonViewModel("Cat");
        var target = new FakeTarget();

        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Friend.Name"), target);
        target.Value.Should().Be("<fallback>", "the friend is null");

        vm.Friend.Value = bob;
        target.Value.Should().Be("Bob");

        bob.Name.Value = "Bobby";
        target.Value.Should().Be("Bobby");

        vm.Friend.Value = cat;
        target.Value.Should().Be("Cat");

        bob.Name.Value = "Ignored";
        target.Value.Should().Be("Cat", "the old friend is no longer observed");
        bob.Name.HasObservers.Should().BeFalse();

        vm.Friend.Value = null;
        target.Value.Should().Be("<fallback>");
        this.errors.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ThreeLevelPath()
    {
        var team = new TeamViewModel();
        team.Captain.Friend.Value = new PersonViewModel("Deputy");
        var target = new FakeTarget();

        using IDisposable binding = this.engine.BindProperty(ContextOf(team), Spec("Captain.Friend.Name"), target);
        target.Value.Should().Be("Deputy");
    }

    [Fact]
    public void DisposedSourceFallsBackAndReleasesOwner()
    {
        var vm = new PersonViewModel();
        var friend = new PersonViewModel("Bob");
        vm.Friend.Value = friend;
        var target = new FakeTarget();

        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Friend.Name"), target);
        target.Value.Should().Be("Bob");

        friend.Dispose();
        target.Value.Should().Be("<fallback>");
    }

    [Fact]
    public void DisposedIntermediateFallsBack()
    {
        var vm = new PersonViewModel();
        vm.Friend.Value = new PersonViewModel("Bob");
        var target = new FakeTarget();

        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Friend.Name"), target);
        vm.Friend.Dispose();

        target.Value.Should().Be("<fallback>");
    }

    [Fact]
    public void EmptyPathBindsContextItself()
    {
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(DataContext.ForItem("item"), Spec(""), target);
        target.Value.Should().Be("item");
    }

    [Fact]
    public void ContextChangesRebind()
    {
        var context = new ReactiveProperty<object?>(new PersonViewModel("Ada"));
        var target = new FakeTarget();

        using IDisposable binding = this.engine.BindProperty(new DataContext(typeof(PersonViewModel), context), Spec("Name"), target);
        context.Value = new PersonViewModel("Bob");
        target.Value.Should().Be("Bob");

        context.Value = null;
        target.Value.Should().Be("<fallback>");
    }

    [Fact]
    public void BaseTypeAccessorIsUsedForSubclasses()
    {
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(new StudentViewModel()), Spec("School"), target);
        target.Value.Should().Be("Hogwarts");
    }

    [Theory]
    [InlineData("Nope", "has no bindable member 'Nope'")]
    [InlineData("Name.Length", "'Name' is a System.String, which is not a [ViewModel]")]
    [InlineData("Age.Something", "can't continue past PersonViewModel.Age")]
    [InlineData("Friend..Name", "empty segment")]
    public void BadPathsReportAndFallBack(string path, string message)
    {
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(new PersonViewModel()), Spec(path), target);

        target.Value.Should().Be("<fallback>");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain(message);
    }

    [Fact]
    public void NonViewModelContextReports()
    {
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(new object()), Spec("Name"), target);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("is not a [ViewModel]");
    }

    [Fact]
    public void TargetRejectingTypeReports()
    {
        var target = new FakeTarget { RejectType = typeof(int) };
        using IDisposable binding = this.engine.BindProperty(ContextOf(new PersonViewModel()), Spec("Age"), target);

        target.Value.Should().Be("<fallback>");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Be("Rejects Int32");
    }

    [Fact]
    public void CompiledPathsAreCached()
    {
        BindingPath.TryCompile(typeof(PersonViewModel), "Friend.Name", out BindingPath? first, out _).Should().BeTrue();
        BindingPath.TryCompile(typeof(PersonViewModel), "Friend.Name", out BindingPath? second, out _).Should().BeTrue();
        second.Should().BeSameAs(first);
        first!.Hops.Should().ContainSingle().Which.Name.Should().Be("Friend");
        first.Leaf!.Name.Should().Be("Name");
    }

    [Fact]
    public void TwoWayWritesTargetChangesBack()
    {
        var vm = new PersonViewModel("Ada");
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Name", BindingMode.TwoWay), target);

        target.UserSets("Grace");
        vm.Name.Value.Should().Be("Grace");
        target.Applied.Should().Equal(["Ada"], "the write back isn't echoed to the target");

        vm.Name.Value = "Linus";
        target.Value.Should().Be("Linus");
    }

    [Fact]
    public void TwoWayWritesThroughPathToCurrentOwner()
    {
        var vm = new PersonViewModel();
        var bob = new PersonViewModel("Bob");
        var cat = new PersonViewModel("Cat");
        vm.Friend.Value = bob;
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Friend.Name", BindingMode.TwoWay), target);

        vm.Friend.Value = cat;
        target.UserSets("Kitty");

        cat.Name.Value.Should().Be("Kitty");
        bob.Name.Value.Should().Be("Bob");
    }

    [Fact]
    public void TwoWayWithoutOwnerIgnoresTargetChanges()
    {
        var vm = new PersonViewModel();
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Friend.Name", BindingMode.TwoWay), target);

        target.UserSets("Nobody");
        this.errors.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TwoWayConvertsBack()
    {
        var vm = new PersonViewModel(age: 5);
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(
            ContextOf(vm),
            Spec("Age", BindingMode.TwoWay, BuiltInConverters.ToStringId),
            target);
        target.Value.Should().Be("5");

        target.UserSets("42");
        vm.Age.Value.Should().Be(42);

        target.UserSets("not a number");
        vm.Age.Value.Should().Be(42, "unparseable text isn't written");
    }

    [Fact]
    public void TwoWayOnReadOnlyMemberIsOneWay()
    {
        var vm = new PersonViewModel(age: 20);
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("IsAdult", BindingMode.TwoWay), target);

        target.Value.Should().Be(true);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("read-only");
    }

    [Fact]
    public void TwoWayWithoutChangeNotificationReports()
    {
        var target = new FakeTarget { HasChangeNotification = false };
        using IDisposable binding = this.engine.BindProperty(ContextOf(new PersonViewModel()), Spec("Name", BindingMode.TwoWay), target);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("no change notification");
    }

    [Fact]
    public void OneTimeAppliesFirstValueOnly()
    {
        var vm = new PersonViewModel("Ada");
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Name", BindingMode.OneTime), target);

        vm.Name.Value = "Grace";
        target.Applied.Should().Equal("Ada");
        vm.Name.HasObservers.Should().BeFalse();
    }

    [Fact]
    public void OneTimeWaitsForAValue()
    {
        var vm = new PersonViewModel();
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Friend.Name", BindingMode.OneTime), target);
        target.Value.Should().Be("<fallback>");

        vm.Friend.Value = new PersonViewModel("Bob");
        vm.Friend.Value = new PersonViewModel("Cat");
        target.Applied.Should().Equal("Bob");
    }

    [Fact]
    public void DisposingUnsubscribesEverything()
    {
        var vm = new PersonViewModel();
        vm.Friend.Value = new PersonViewModel();
        var target = new FakeTarget();
        IDisposable binding = this.engine.BindProperty(ContextOf(vm), Spec("Friend.Name", BindingMode.TwoWay), target);
        vm.Friend.HasObservers.Should().BeTrue();

        binding.Dispose();

        vm.Friend.HasObservers.Should().BeFalse();
        vm.Friend.Value!.Name.HasObservers.Should().BeFalse();
        target.UserSets("after");
        vm.Friend.Value.Name.Value.Should().Be("");
    }

    [Fact]
    public void ParentContextSource()
    {
        var team = new TeamViewModel();
        var parent = DataContext.Constant(team);
        var child = DataContext.ForItem("row", parent);
        var target = new FakeTarget();

        using IDisposable binding = this.engine.BindProperty(
            child,
            new BindingSpec { Path = "Captain.Name", Source = BindingSourceKind.ParentContext },
            target);
        target.Value.Should().Be("Cap");
    }

    [Fact]
    public void ParentContextWithoutParentReports()
    {
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(
            ContextOf(new PersonViewModel()),
            new BindingSpec { Path = "Name", Source = BindingSourceKind.ParentContext },
            target);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("no parent");
    }

    [Fact]
    public void CommandAsPropertyReports()
    {
        var target = new FakeTarget();
        using IDisposable binding = this.engine.BindProperty(ContextOf(new PersonViewModel()), Spec("Greet"), target);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("is a command");
    }
}

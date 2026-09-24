using AwesomeAssertions;
using GodotHat.Binding.Core.Test.Support;

namespace GodotHat.Binding.Core.Test;

public class HintAndRegistryTest
{
    [Fact]
    public void ParsesRangeHints()
    {
        RangeHint.TryParse("0,100,1,or_greater,suffix:px", out RangeHint hint).Should().BeTrue();
        hint.Should().Be(new RangeHint(0, 100, 1, OrGreater: true, OrLess: false));
        hint.Contains(150).Should().BeTrue();
        hint.Contains(-1).Should().BeFalse();

        RangeHint.TryParse("-1.5,1.5", out hint).Should().BeTrue();
        hint.Step.Should().BeNull();
        RangeHint.TryParse("0,1,or_less", out hint).Should().BeTrue();
        hint.Step.Should().BeNull();
        hint.OrLess.Should().BeTrue();

        RangeHint.TryParse("", out _).Should().BeFalse();
        RangeHint.TryParse("oops", out _).Should().BeFalse();
    }

    [Fact]
    public void ParsesEnumHintsWithImplicitValuesContinuing()
    {
        HintOption.ParseEnum("Zero,One,Three:3,Four,Six:6").Should().Equal(
            new HintOption("Zero", 0),
            new HintOption("One", 1),
            new HintOption("Three", 3),
            new HintOption("Four", 4),
            new HintOption("Six", 6));
        HintOption.ParseEnum(null).Should().BeEmpty();
    }

    [Fact]
    public void ParsesFlagsHintsWithImplicitValuesByIndex()
    {
        HintOption.ParseFlags("Fill,Expand,Shrink Center:4,Shrink End:8").Should().Equal(
            new HintOption("Fill", 1),
            new HintOption("Expand", 2),
            new HintOption("Shrink Center", 4),
            new HintOption("Shrink End", 8));
        HintOption.ParseFlags("A:16,B,C").Should().Equal(
            new HintOption("A", 16),
            new HintOption("B", 2),
            new HintOption("C", 4));
    }

    [Fact]
    public void RegistryResolvesByTypeBaseTypeAndName()
    {
        BindingRegistry.TryGet(typeof(PersonViewModel), out ViewModelAccessor? person).Should().BeTrue();
        person.Should().BeSameAs(PersonViewModel.Accessor);
        BindingRegistry.TryGet(typeof(StudentViewModel), out ViewModelAccessor? student).Should().BeTrue();
        student.Should().BeSameAs(PersonTypeBase.Accessor);
        BindingRegistry.TryGet(typeof(string), out _).Should().BeFalse();

        BindingRegistry.TryGetByName(typeof(TeamViewModel).FullName!, out ViewModelAccessor? team).Should().BeTrue();
        team.Should().BeSameAs(TeamViewModel.Accessor);
        BindingRegistry.Accessors.Should().Contain(PersonViewModel.Accessor);
    }

    [Fact]
    public void AccessorDescribesMembers()
    {
        PersonViewModel.Accessor.TryGetMember("Age", out MemberAccessor? age).Should().BeTrue();
        age!.Kind.Should().Be(MemberKind.ReactiveProperty);
        age.ValueType.Should().Be(typeof(int));
        ((ValueMember)age).CanWrite.Should().BeTrue();

        PersonViewModel.Accessor.TryGetMember("AddYears", out MemberAccessor? addYears).Should().BeTrue();
        addYears!.Kind.Should().Be(MemberKind.Command);
        addYears.ValueType.Should().Be(typeof(int));
    }

    [Fact]
    public void DefaultActivatorUsesGeneratedFactory()
    {
        DefaultViewModelActivator.Instance.Create(typeof(PersonViewModel)).Should().BeOfType<PersonViewModel>();

        FluentActions.Invoking(() => DefaultViewModelActivator.Instance.Create(typeof(TeamViewModel)))
            .Should().Throw<InvalidOperationException>().WithMessage("*no public parameterless constructor*");
        FluentActions.Invoking(() => DefaultViewModelActivator.Instance.Create(typeof(StudentViewModel)))
            .Should().Throw<InvalidOperationException>().WithMessage("*is not a [ViewModel]*");
    }

    [Fact]
    public void ServiceProviderActivatorPrefersServiceFactory()
    {
        var services = new SingleServices("injected");
        var withServices = new ViewModelAccessor(
            typeof(ServiceUser),
            [],
            static () => new ServiceUser("default"),
            static sp => new ServiceUser(ServiceResolver.Required<string>(sp, typeof(ServiceUser))));
        BindingRegistry.Register(withServices);
        var activator = new ServiceProviderViewModelActivator(services);

        ((ServiceUser)activator.Create(typeof(ServiceUser))).Value.Should().Be("injected");
        activator.Create(typeof(PersonViewModel)).Should().BeOfType<PersonViewModel>("without a service factory the parameterless constructor is used");
        ServiceResolver.Optional<int?>(services).Should().BeNull();
        FluentActions.Invoking(() => ServiceResolver.Required<Uri>(services, typeof(ServiceUser)))
            .Should().Throw<InvalidOperationException>().WithMessage("ServiceUser needs a Uri, which the service provider doesn't provide.");
    }

    private sealed class ServiceUser(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class SingleServices(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
    }

    [Fact]
    public void EnumTables()
    {
        BindingRegistry.TryGetEnum(out IEnumInfo<Mood>? mood).Should().BeTrue();
        mood!.GetName(Mood.Sad).Should().Be("Sad");
        mood.GetName((Mood)99).Should().BeNull();
        mood.IndexOf(Mood.Grumpy).Should().Be(2);
        mood.TryParse("Grumpy", out Mood parsed).Should().BeTrue();
        parsed.Should().Be(Mood.Grumpy);
        BindingRegistry.TryGetEnum(out IEnumInfo<int>? _).Should().BeFalse();
    }

    [Fact]
    public void ViewModelBaseDisposesBag()
    {
        var vm = new CounterViewModel();
        vm.Dispose();
        vm.IsDisposed.Should().BeTrue();
        vm.Count.IsDisposed.Should().BeTrue();
    }

    private sealed class CounterViewModel : ViewModelBase
    {
        public R3.ReactiveProperty<int> Count { get; }

        public CounterViewModel()
        {
            this.Count = new R3.ReactiveProperty<int>();
            this.Bag.Add(this.Count);
        }
    }
}

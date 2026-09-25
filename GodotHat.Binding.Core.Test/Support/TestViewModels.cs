using System.Runtime.CompilerServices;
using R3;

namespace GodotHat.Binding.Core.Test.Support;

public enum Mood
{
    Happy,
    Sad,
    Grumpy,
}

// View models with hand-written accessors, standing in for generated ones.
public sealed class PersonViewModel : IDisposable
{
    public ReactiveProperty<string> Name { get; } = new("");
    public ReactiveProperty<int> Age { get; } = new();
    public ReadOnlyReactiveProperty<bool> IsAdult { get; }
    public ReactiveProperty<Mood> Mood { get; } = new();
    public ReactiveProperty<PersonViewModel?> Friend { get; } = new();
    public Subject<string> Messages { get; } = new();
    public ReactiveCommand Greet { get; } = new();
    public ReactiveCommand<int> AddYears { get; }
    public ReactiveProperty<bool> CanAddYears { get; } = new(true);
    public string Id { get; }

    public List<int> Added { get; } = [];

    public PersonViewModel(string name = "", int age = 0, string id = "p")
    {
        this.Name.Value = name;
        this.Age.Value = age;
        this.Id = id;
        this.IsAdult = this.Age.Select(a => a >= 18).ToReadOnlyReactiveProperty();
        this.AddYears = this.CanAddYears.ToReactiveCommand<int>(years =>
        {
            this.Added.Add(years);
            this.Age.Value += years;
        });
    }

    public void Dispose()
    {
        this.Name.Dispose();
        this.Age.Dispose();
        this.IsAdult.Dispose();
        this.Mood.Dispose();
        this.Friend.Dispose();
        this.Messages.Dispose();
        this.Greet.Dispose();
        this.AddYears.Dispose();
        this.CanAddYears.Dispose();
    }

    public static readonly ViewModelAccessor Accessor = new(
        typeof(PersonViewModel),
        [
            new PropertyMember<PersonViewModel, string>("Name", MemberKind.ReactiveProperty, static vm => vm.Name, static (vm, v) => vm.Name.Value = v),
            new PropertyMember<PersonViewModel, int>("Age", MemberKind.ReactiveProperty, static vm => vm.Age, static (vm, v) => vm.Age.Value = v),
            new PropertyMember<PersonViewModel, bool>("IsAdult", MemberKind.ReadOnlyProperty, static vm => vm.IsAdult),
            new PropertyMember<PersonViewModel, Mood>("Mood", MemberKind.ReactiveProperty, static vm => vm.Mood, static (vm, v) => vm.Mood.Value = v),
            new PropertyMember<PersonViewModel, PersonViewModel?>("Friend", MemberKind.ReactiveProperty, static vm => vm.Friend, static (vm, v) => vm.Friend.Value = v),
            new PropertyMember<PersonViewModel, string>("Messages", MemberKind.Observable, static vm => vm.Messages),
            new CommandMember<PersonViewModel, Unit>("Greet", static vm => vm.Greet, static (vm, p) => vm.Greet.Execute(p)),
            new CommandMember<PersonViewModel, int>("AddYears", static vm => vm.AddYears, static (vm, p) => vm.AddYears.Execute(p)),
            new ConstantMember<PersonViewModel, string>("Id", static vm => vm.Id),
        ],
        static () => new PersonViewModel());
}

public sealed class TeamViewModel
{
    public ReactiveProperty<PersonViewModel?> Leader { get; } = new();
    public PersonViewModel Captain { get; } = new("Cap", 30);
    public ReactiveCommand<PersonViewModel> Promote { get; }

    public TeamViewModel()
    {
        this.Promote = new ReactiveCommand<PersonViewModel>(person => this.Leader.Value = person);
    }

    public static readonly ViewModelAccessor Accessor = new(
        typeof(TeamViewModel),
        [
            new PropertyMember<TeamViewModel, PersonViewModel?>("Leader", MemberKind.ReactiveProperty, static vm => vm.Leader, static (vm, v) => vm.Leader.Value = v),
            new ConstantMember<TeamViewModel, PersonViewModel>("Captain", static vm => vm.Captain),
            new CommandMember<TeamViewModel, PersonViewModel>("Promote", static vm => vm.Promote, static (vm, p) => vm.Promote.Execute(p)),
        ]);
}

// Registered through its base type's accessor.
public sealed class StudentViewModel() : PersonTypeBase;

public class PersonTypeBase
{
    public ReactiveProperty<string> School { get; } = new("Hogwarts");

    public static readonly ViewModelAccessor Accessor = new(
        typeof(PersonTypeBase),
        [new PropertyMember<PersonTypeBase, string>("School", MemberKind.ReactiveProperty, static vm => vm.School)]);
}

internal static class TestRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        BindingRegistry.Register(PersonViewModel.Accessor);
        BindingRegistry.Register(TeamViewModel.Accessor);
        BindingRegistry.Register(PersonTypeBase.Accessor);
        BindingRegistry.Register(InventoryViewModel.Accessor);
        BindingRegistry.RegisterEnum(new EnumInfo<Mood>([Mood.Happy, Mood.Sad, Mood.Grumpy], ["Happy", "Sad", "Grumpy"]));
    }
}

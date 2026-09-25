using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using R3;

namespace GodotHat.Binding.SourceGenerators.Test;

// Compiles view models with their generated accessors, then binds them through the real engine.
public class GeneratedAccessorRuntimeTest
{
    private const string Source = """
        using GodotHat.Binding;
        using ObservableCollections;
        using R3;

        namespace Runtime;

        public enum Difficulty { Easy, Normal, Hard }

        public interface IArmory
        {
            int Swords { get; }
        }

        [ViewModel]
        public sealed class ArmoryViewModel(IArmory armory)
        {
            public int Swords => armory.Swords;
        }

        [ViewModel]
        public sealed class PlayerViewModel
        {
            public ReactiveProperty<int> Health { get; } = new(100);
            public ReactiveProperty<Difficulty> Difficulty { get; } = new(Runtime.Difficulty.Normal);
            public ReactiveProperty<PlayerViewModel?> Target { get; } = new();
            public ReactiveCommand<int> Damage { get; }
            public string Name => "Hero";
            public ObservableList<string> Inventory { get; } = new();

            public PlayerViewModel()
            {
                Damage = new ReactiveCommand<int>(amount => Health.Value -= amount);
            }
        }
        """;

    private static readonly Lazy<Assembly> Compiled = new(() => GeneratorHarness.CompileAndLoad(Source));

    private static object NewPlayer()
    {
        Type type = Compiled.Value.GetType("Runtime.PlayerViewModel")!;
        BindingRegistry.TryGet(type, out ViewModelAccessor? accessor).Should().BeTrue();
        return accessor!.CreateInstance();
    }

    private static readonly BindingEngine Engine = new(BindingDispatcher.Immediate, new ThrowingSink());

    [Fact]
    public void AccessorDescribesMembers()
    {
        Type type = Compiled.Value.GetType("Runtime.PlayerViewModel")!;
        BindingRegistry.TryGet(type, out ViewModelAccessor? accessor).Should().BeTrue();

        accessor!.Members.Select(m => (m.Name, m.Kind, m.ValueType.Name)).Should().Equal(
            ("Health", MemberKind.ReactiveProperty, "Int32"),
            ("Difficulty", MemberKind.ReactiveProperty, "Difficulty"),
            ("Target", MemberKind.ReactiveProperty, "PlayerViewModel"),
            ("Damage", MemberKind.Command, "Int32"),
            ("Name", MemberKind.Constant, "String"),
            ("Inventory", MemberKind.Items, "String"));
        accessor.CanCreateInstance.Should().BeTrue();
    }

    [Fact]
    public void ServiceProviderActivatorInjectsConstructorParameters()
    {
        Type armoryType = Compiled.Value.GetType("Runtime.IArmory")!;
        Type viewModelType = Compiled.Value.GetType("Runtime.ArmoryViewModel")!;
        object armory = System.Reflection.DispatchProxy.Create(armoryType, typeof(SwordsProxy));
        var services = new DictionaryServices(new() { [armoryType] = armory });

        object vm = new ServiceProviderViewModelActivator(services).Create(viewModelType);
        var swords = new CapturingTarget<int>();
        using IDisposable binding = Engine.BindProperty(DataContext.Constant(vm), new BindingSpec { Path = "Swords" }, swords);

        swords.Value.Should().Be(3);
        FluentActions.Invoking(() => new ServiceProviderViewModelActivator(new DictionaryServices(new())).Create(viewModelType))
            .Should().Throw<InvalidOperationException>().WithMessage("ArmoryViewModel needs a IArmory, which the service provider doesn't provide.");
    }

    public class SwordsProxy : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => 3;
    }

    private sealed class DictionaryServices(Dictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);
    }

    [Fact]
    public void BindsThroughGeneratedAccessors()
    {
        object player = NewPlayer();
        object target = NewPlayer();
        var health = new CapturingTarget<int>();
        var difficulty = new CapturingTarget<string>();
        var targetName = new CapturingTarget<string>();
        var damage = new PressTarget();

        using IDisposable b1 = Engine.BindProperty(DataContext.Constant(player), new BindingSpec { Path = "Health" }, health);
        using IDisposable b2 = Engine.BindProperty(
            DataContext.Constant(player),
            new BindingSpec { Path = "Difficulty", Converter = BuiltInConverters.EnumNameId, Mode = BindingMode.TwoWay },
            difficulty);
        using IDisposable b3 = Engine.BindProperty(DataContext.Constant(player), new BindingSpec { Path = "Target.Name" }, targetName);
        using IDisposable b4 = Engine.BindCommand(
            DataContext.Constant(player),
            new BindingSpec { Path = "Damage", Parameter = BindingParameterKind.Constant, ParameterValue = "30" },
            damage);

        health.Value.Should().Be(100);
        difficulty.Value.Should().Be("Normal", "the generated enum table names the value");
        targetName.HasFallback.Should().BeTrue();

        damage.Press();
        health.Value.Should().Be(70);

        difficulty.UserSets("Hard");
        ((ReactiveProperty<int>)player.GetType().GetProperty("Health")!.GetValue(player)!).Value.Should().Be(70);
        player.GetType().GetProperty("Difficulty")!.GetValue(player)!.ToString().Should().Be("Hard");

        object targetProperty = player.GetType().GetProperty("Target")!.GetValue(player)!;
        targetProperty.GetType().GetProperty("Value")!.SetValue(targetProperty, target);
        targetName.Value.Should().Be("Hero");
    }

    [Fact]
    public void UpdatesThroughGeneratedAccessorsDoNotAllocate()
    {
        object player = NewPlayer();
        var health = new CapturingTarget<int>();
        using IDisposable binding = Engine.BindProperty(DataContext.Constant(player), new BindingSpec { Path = "Health" }, health);
        var property = (ReactiveProperty<int>)player.GetType().GetProperty("Health")!.GetValue(player)!;

        for (int i = 0; i < 1000; i++)
        {
            property.Value = i;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1000; i < 2000; i++)
        {
            property.Value = i;
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
        health.Value.Should().Be(1999);
    }

    private sealed class ThrowingSink : IBindingErrorSink
    {
        public void Report(in BindingError error) => throw new InvalidOperationException(error.ToString());
    }

    private sealed class CapturingTarget<TValue> : IBindingTarget
    {
        private Action? onChanged;

        public TValue Value = default!;
        public bool HasFallback;

        public string Description => "target";

        public bool CanAccept<T>([NotNullWhen(false)] out string? error)
        {
            error = typeof(T) == typeof(TValue) ? null : $"Expected {typeof(TValue).Name}, got {typeof(T).Name}";
            return error is null;
        }

        public void SetValue<T>(T value)
        {
            this.Value = Unsafe.As<T, TValue>(ref value);
            this.HasFallback = false;
        }

        public void SetFallback() => this.HasFallback = true;

        public bool TryReadValue<T>(out T value)
        {
            value = Unsafe.As<TValue, T>(ref this.Value);
            return true;
        }

        public IDisposable? ObserveChanges(Action onChanged)
        {
            this.onChanged = onChanged;
            return Disposable.Empty;
        }

        public void UserSets(TValue value)
        {
            this.Value = value;
            this.onChanged?.Invoke();
        }
    }

    private sealed class PressTarget : ICommandTarget
    {
        private Action? onInvoked;

        public string Description => "button";

        public IDisposable ObserveInvoked(Action onInvoked)
        {
            this.onInvoked = onInvoked;
            return Disposable.Empty;
        }

        public void SetAvailable(bool available)
        {
        }

        public void Press() => this.onInvoked?.Invoke();
    }
}

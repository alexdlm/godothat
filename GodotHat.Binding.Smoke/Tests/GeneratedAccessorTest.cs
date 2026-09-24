namespace GodotHat.Binding.Smoke.Tests;

using GdUnit4;
using GodotHat.Binding.Smoke.ViewModels;
using static GdUnit4.Assertions;

// The view model generator ships in GodotHat.Binding.Core's package; this checks it runs in a Godot project build.
[TestSuite]
[RequireGodotRuntime]
public class GeneratedAccessorTest
{
    [TestCase]
    public void AccessorIsRegisteredByModuleInitializer()
    {
        AssertThat(BindingRegistry.TryGet(typeof(CounterViewModel), out ViewModelAccessor? accessor)).IsTrue();
        AssertThat(accessor!.CanCreateInstance).IsTrue();
        AssertThat(accessor.TryGetMember("Count", out _)).IsTrue();
        AssertThat(accessor.TryGetMember("Increment", out _)).IsTrue();
        AssertThat(BindingRegistry.TryGetEnum(out IEnumInfo<Speed>? speed)).IsTrue();
        AssertThat(speed!.GetName(Speed.Fast)).IsEqual("Fast");
    }

    [TestCase]
    public void ActivatorCreatesViewModel()
    {
        using var vm = (CounterViewModel)DefaultViewModelActivator.Instance.Create(typeof(CounterViewModel));
        vm.Increment.Execute(R3.Unit.Default);
        AssertThat(vm.Count.Value).IsEqual(1);
    }
}

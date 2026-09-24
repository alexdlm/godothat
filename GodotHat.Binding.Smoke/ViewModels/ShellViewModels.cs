namespace GodotHat.Binding.Smoke.ViewModels;

using Jab;
using R3;

[ViewModel]
public sealed class ShellViewModel : ViewModelBase
{
    public ShellViewModel()
    {
        this.Page = new ReactiveProperty<object?>().AddTo(this.Bag);
    }

    public ReactiveProperty<object?> Page { get; }
}

[ViewModel]
public sealed class HomePageViewModel : ViewModelBase
{
    public string Title => "Home";
}

[ViewModel]
public sealed class AboutPageViewModel : ViewModelBase
{
    public string Title => "About";
}

public interface IGreeter
{
    string Greet(string name);
}

public sealed class Greeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}";
}

// View models take services by constructor; they're created through the activator, not registered in Jab, so Jab
// never tracks or disposes them.
[ViewModel]
public sealed class GreetingViewModel(IGreeter greeter) : ViewModelBase
{
    public string Greeting => greeter.Greet("Godot");
}

[ServiceProvider]
[Singleton(typeof(IGreeter), typeof(Greeter))]
public partial class SmokeServices;

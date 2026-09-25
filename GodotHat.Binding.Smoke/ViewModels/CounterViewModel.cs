namespace GodotHat.Binding.Smoke.ViewModels;

using R3;

public enum Speed
{
    Slow,
    Fast,
}

[ViewModel]
public sealed class CounterViewModel : ViewModelBase
{
    public ReactiveProperty<int> Count { get; }
    public ReactiveProperty<Speed> Speed { get; }
    public ReadOnlyReactiveProperty<bool> IsPositive { get; }
    public ReactiveCommand Increment { get; }

    public CounterViewModel()
    {
        this.Count = new ReactiveProperty<int>().AddTo(this.Bag);
        this.Speed = new ReactiveProperty<Speed>().AddTo(this.Bag);
        this.IsPositive = this.Count.Select(c => c > 0).ToReadOnlyReactiveProperty().AddTo(this.Bag);
        this.Increment = new ReactiveCommand(_ => this.Count.Value++).AddTo(this.Bag);
    }
}

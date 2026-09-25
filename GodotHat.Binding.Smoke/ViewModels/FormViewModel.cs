namespace GodotHat.Binding.Smoke.ViewModels;

using R3;

[ViewModel]
public sealed class FormViewModel : ViewModelBase
{
    public ReactiveProperty<string> Name { get; }
    public ReactiveProperty<double> Volume { get; }
    public ReactiveProperty<bool> Enabled { get; }
    public ReactiveProperty<Speed> Speed { get; }
    public ReactiveProperty<int> Count { get; }
    public ReactiveProperty<FormViewModel?> Child { get; }
    public ReactiveCommand Increment { get; }
    public ReactiveCommand Reset { get; }
    public Subject<string> Flashed { get; }

    public FormViewModel()
    {
        this.Name = new ReactiveProperty<string>("").AddTo(this.Bag);
        this.Volume = new ReactiveProperty<double>().AddTo(this.Bag);
        this.Enabled = new ReactiveProperty<bool>().AddTo(this.Bag);
        this.Speed = new ReactiveProperty<Speed>().AddTo(this.Bag);
        this.Count = new ReactiveProperty<int>().AddTo(this.Bag);
        this.Child = new ReactiveProperty<FormViewModel?>().AddTo(this.Bag);
        this.Increment = new ReactiveCommand(_ => this.Count.Value++).AddTo(this.Bag);
        this.Reset = this.Count.Select(c => c > 0).ToReactiveCommand(_ => this.Count.Value = 0, false).AddTo(this.Bag);
        this.Flashed = new Subject<string>().AddTo(this.Bag);
    }
}

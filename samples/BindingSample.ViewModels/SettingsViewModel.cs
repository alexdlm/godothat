namespace BindingSample.ViewModels;

using GodotHat.Binding;
using ObservableCollections;
using R3;

public enum Difficulty
{
    Relaxed,
    Normal,
    Brutal,
}

// A row in the save history. Rows can be immutable records; changing one means replacing it in the list.
[ViewModel]
public sealed record SaveEntry(int Number, string Summary)
{
    public string Title => $"#{this.Number} {this.Summary}";
}

// A settings page. Its scene binds to it without a view class: see Settings.tscn. The store and clock come from the
// service provider through the constructor, so tests can pass fakes.
[ViewModel]
public sealed class SettingsViewModel : ViewModelBase
{
    public static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(3);

    private readonly ISettingsStore store;
    private readonly TimeProvider time;
    private readonly SerialDisposable clearStatus;
    private readonly ReactiveProperty<bool> saving;

    public SettingsViewModel(ISettingsStore store, TimeProvider time)
    {
        this.store = store;
        this.time = time;
        this.clearStatus = new SerialDisposable().AddTo(this.Bag);
        this.saving = new ReactiveProperty<bool>().AddTo(this.Bag);
        this.PlayerName = new ReactiveProperty<string>("Ada").AddTo(this.Bag);
        this.Volume = new ReactiveProperty<double>(70).AddTo(this.Bag);
        this.Difficulty = new ReactiveProperty<Difficulty>(ViewModels.Difficulty.Normal).AddTo(this.Bag);
        this.Fullscreen = new ReactiveProperty<bool>().AddTo(this.Bag);
        this.Saves = new ReactiveProperty<int>().AddTo(this.Bag);
        this.Status = new Subject<string>().AddTo(this.Bag);

        this.Summary = this.PlayerName
            .CombineLatest(this.Difficulty, this.Volume, (name, difficulty, volume) => $"{name}: {difficulty}, volume {volume:0}%")
            .ToReadOnlyReactiveProperty("")
            .AddTo(this.Bag);
        this.IsDirty = new ReactiveProperty<bool>().AddTo(this.Bag);
        Observable.Merge(
                this.PlayerName.Skip(1).AsUnitObservable(),
                this.Volume.Skip(1).AsUnitObservable(),
                this.Difficulty.Skip(1).AsUnitObservable(),
                this.Fullscreen.Skip(1).AsUnitObservable())
            .Subscribe(_ => this.IsDirty.Value = true)
            .AddTo(this.Bag);

        this.Save = this.IsDirty
            .CombineLatest(this.saving, (dirty, saving) => dirty && !saving)
            .ToReactiveCommand(_ => this.SaveInBackground(), false)
            .AddTo(this.Bag);
        this.ResetName = new ReactiveCommand(_ => this.PlayerName.Value = "Ada").AddTo(this.Bag);
    }

    public ReactiveProperty<string> PlayerName { get; }
    public ReactiveProperty<double> Volume { get; }
    public ReactiveProperty<Difficulty> Difficulty { get; }
    public ReactiveProperty<bool> Fullscreen { get; }
    public ReactiveProperty<int> Saves { get; }
    public ReadOnlyReactiveProperty<string> Summary { get; }
    public ReactiveProperty<bool> IsDirty { get; }
    public ReadOnlyReactiveProperty<bool> IsSaving => this.saving;
    public ReactiveCommand Save { get; }

    // Newest first, shown by an @items binding with SaveRow.tscn as the template.
    public ObservableList<SaveEntry> History { get; } = new();
    public ReactiveCommand ResetName { get; }

    // Shown by an event binding, which calls the status label's set_text. Each message clears after StatusDuration.
    public Subject<string> Status { get; }

    // Saving finishes on a worker thread; the binding layer applies the updates on the main thread.
    private void SaveInBackground()
    {
        this.saving.Value = true;
        Task.Run(async () =>
        {
            string message;
            try
            {
                await this.store.SaveAsync(this.Summary.CurrentValue);
                this.Saves.Value++;
                this.History.Insert(0, new SaveEntry(this.Saves.Value, this.Summary.CurrentValue));
                this.IsDirty.Value = false;
                message = $"Saved {this.Saves.Value} time(s)";
            }
            catch (Exception e)
            {
                message = $"Couldn't save: {e.Message}";
            }

            this.saving.Value = false;
            this.ShowStatus(message);
        });
    }

    private void ShowStatus(string message)
    {
        this.Status.OnNext(message);
        this.clearStatus.Disposable = Observable.Timer(StatusDuration, this.time).Subscribe(this.Status, static (_, status) => status.OnNext(""));
    }
}

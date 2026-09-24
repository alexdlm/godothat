namespace GodotHat.Binding.Smoke.ViewModels;

using ObservableCollections;
using R3;

[ViewModel]
public sealed class TodoItem(string name)
{
    public ReactiveProperty<string> Name { get; } = new(name);

    public override string ToString() => this.Name.Value;
}

// Rows needn't be view models with reactive members: records are replaced in the collection to change them.
[ViewModel]
public sealed record Tag(string Label, int Uses);

[ViewModel]
public sealed class TodoListViewModel : ViewModelBase
{
    public TodoListViewModel()
    {
        this.Selected = new ReactiveProperty<TodoItem?>().AddTo(this.Bag);
        this.Remove = new ReactiveCommand<TodoItem>(item => this.Items.Remove(item)).AddTo(this.Bag);
    }

    public ObservableList<TodoItem> Items { get; } = new();
    public ObservableList<Tag> Tags { get; } = new();
    public ReactiveProperty<TodoItem?> Selected { get; }
    public ReactiveCommand<TodoItem> Remove { get; }
}

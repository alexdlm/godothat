# godothat


Alternate C# source generators for godot.

## Attributes

### OnReady

Implements `_Ready` for you. More interesting if you return an `IDisposable`

```csharp
[OnReady]
private void DoReadyThings()
{
  // do your OnReady things
}
```

That's mildly useful, mostly down to style, though not much reason to not just override _Ready yourself. 
But godothat will also perform automatic cleanup of IDisposables on tree exit:

```csharp
[OnReady]
private IDisposable SubscribeToFoos() => FooManager.WhenFoosUpdated.Subscribe(this.FoosUpdated);

private void FoosUpdated(IEnumerable<Foo> foos) {
  // Do things with the new Foos 
}
```

You can have multiple `[OnReady]` methods.

### OnEnterTree / OnExitTree
Like `[OnReady]`, but for EnterTree. `[OnExitTree]` for other explicit cleanup options. 
```csharp
[OnEnterTree]
private void ConnectMultiplayerEvents() {
   var multiplayer = GetTree().GetMultiplayer();
   multiplayer.PeerConnected += this.OnNetworkPeerConnected;
   multiplayer.PeerDisconnected += this.OnNetworkPeerDisconnected;
}

[OnExitTree]
private void CleanupMultiplayerEvents() {
   var multiplayer = GetTree().GetMultiplayer();
   multiplayer.PeerConnected -= this.OnNetworkPeerConnected;
   multiplayer.PeerDisconnected -= this.OnNetworkPeerDisconnected;
}
```

Or with an `IDisposable`:

```csharp
[OnEnterTree]
private IDisposable SetupMultiplayerEvents() {
    var multiplayer = GetTree().GetMultiplayer();
    multiplayer.PeerConnected += this.OnNetworkPeerConnected;
    multiplayer.PeerDisconnected += this.OnNetworkPeerDisconnected;
    return Disposable.Create(() => {
        multiplayer.PeerConnected -= this.OnNetworkPeerConnected;
        multiplayer.PeerDisconnected -= this.OnNetworkPeerDisconnected;
    });
}
```

### SceneUniqueName

Automatically resolve named nodes within children on enter tree, on fields or properties.

```csharp
[SceneUniqueName("%MyLabel", required: false)]
private RichTextLabel? OptionalLabel { get; set; }

[SceneUniqueName("%MyLabel")]
private RichTextLabel Label { get; set; }

[SceneUniqueName]
private RichTextLabel MyLabel;
```

This resolves via `GetNode` (or `GetNodeOrNull` if not required) in a generated `_EnterTree`, so you will get useful
godot errors if it is not found.

Technically you don't have to limit this to Unique Names (ie `%...`), but that is what it was intended for and given it
is called in the EnterTree lifecycle you may need to be cautious.

### AutoDispose

Wraps a method with an `Update` version, and tracks cleanup via an `IDisposable`.

[`[OnReady]`](#onready) and [`[OnEnterTree]`](#onentertree--onexittree) above described some utility in returning
`IDisposable`, however this allows disposable tracking for ad-hoc methods that will still get cleaned up.

For example

```csharp
[AutoDispose]
private IDisposable Foo(Foo foo) {
  this.foo = foo;
  foo.Add(this);
  return Disposable.Create(() => {
    foo.Remove(this);
    this.foo = null;
  });
}

// This will automatically create UpdateFoo and DisposeFoo methods.
public void UpdateFoo(Foo foo);
private void DisposeFoo();
```

Accessibility of the `Update` method can be controlled with the `accessibility` argument, eg
`[AutoDispose(Accessibility.Private)]`.

DisposeFoo will be called on tree exit.

### GodotIgnore

GodotHat replaces Godot's `ScriptMethods` source generator (see [Using godothat](#using-godothat)), which exposes
methods with Variant-compatible parameters and return values to Godot so they can be called from the engine, GDScript,
`Call()`, etc. Use `[GodotIgnore]` to keep a method from being exposed.

```csharp
[GodotIgnore]
public void NotCallableFromGodot() { }
```

## Other notes

Ordering of calls is by occurrence within the source file, and in reverse on dispose (if applicable).
This means you likely want `[SceneUniqueName]` fields and properties before any `[OnEnterTree]` methods
that depend on them.

```csharp
[SceneUniqueName("%MyNode")]
Node MyNodePopulatedFirst;

[OnEnterTree]
IDisposable FirstMethod() { /* ... */ }

[OnEnterTree]
IDisposable? SecondMethod() { /* ... */ }

[OnEnterTree]
void ThirdMethod() { /* ... */ }

// Generated _EnterTree is conceptually:
public override void _EnterTree()
{
  base._EnterTree();
  MyNodePopulatedFirst = GetNode<Node>("%MyNode");
  __disposable_FirstMethod = FirstMethod();
  __disposable_SecondMethod = SecondMethod();
  ThirdMethod();
}

// Generated _ExitTree is conceptually:
public override void _ExitTree()
{
  __disposable_SecondMethod?.Dispose();
  __disposable_SecondMethod = null;
  __disposable_FirstMethod?.Dispose();
  __disposable_FirstMethod = null;
  base._ExitTree();
}
```

Generated overrides call the base implementation, first on the way in (`_EnterTree`, `_Ready`) and last on the way
out (`_ExitTree`), so GodotHat attributes work on subclasses of nodes that have their own lifecycle overrides.

## Data binding

`GodotHat.Binding` (in progress) binds Controls to view models built on [R3](https://github.com/Cysharp/R3) and
[ObservableCollections](https://github.com/Cysharp/ObservableCollections). Bindings are stored on each Control, so there
is no per-screen view class, and updates flow with their concrete types, with no reflection or boxing.

See [samples/BindingSample](samples/BindingSample) for a complete screen.

### View models

A view model is any class marked `[ViewModel]` that exposes R3 members. There is no required base class and nothing is
generated into the class, so it need not be `partial`, and can even be a Node.

```csharp
[ViewModel]
public sealed class PlayerViewModel : ViewModelBase
{
    public ReactiveProperty<string> Name { get; } = new("Ada");      // two-way
    public ReadOnlyReactiveProperty<bool> IsAlive { get; }           // one-way
    public Subject<string> Shouted { get; } = new();                 // events
    public ReactiveCommand Heal { get; }                             // commands, with availability

    public PlayerViewModel(IHealthService health)
    {
        IsAlive = health.Hp.Select(hp => hp > 0).ToReadOnlyReactiveProperty().AddTo(Bag);
        Heal = IsAlive.ToReactiveCommand(_ => health.Heal()).AddTo(Bag);
    }
}
```

Members bind by type: `ReactiveProperty<T>` (and `BindableReactiveProperty<T>`) read and write,
`ReadOnlyReactiveProperty<T>` reads, other `Observable<T>`s drive events, `ReactiveCommand`s bind to signals, and
properties of another `[ViewModel]` type can be followed with dotted paths such as `Selected.Name`. `[NotBindable]`
hides a member. The optional `ViewModelBase` just provides `Bag`, a `CompositeDisposable` disposed with the view model.

A source generator, shipped in the `GodotHat.Binding.Core` package, emits a typed accessor for each view model and
registers it when the assembly loads, so view models can live in assemblies without Godot; scenes can name them by type
before any of their code has run. It reports:

| Id      | Severity | Problem                                                                                  |
|---------|----------|------------------------------------------------------------------------------------------|
| GHB0001 | Warning  | A reactive member has a public setter; replacing the instance doesn't update bindings    |
| GHB0002 | Info     | A member's type can't be passed to Godot directly, so it needs a converter               |
| GHB0003 | Warning  | Two members' names differ only by case                                                   |
| GHB0004 | Error    | The view model is private or protected, so its accessor can't be generated               |
| GHB0005 | Warning  | The view model is generic, which isn't supported; mark a non-generic subclass instead    |
| GHB0006 | Warning  | A view list (`ToViewList`) can't be bound; expose the collection or view instead         |

### Converters

Bindings can name a converter: `not` (or `invert`), `not_null`, `is_null`, `to_string`, `format` (with a format string
argument, eg `HP: {0:N0}`), `equals` (true when the value equals the argument, eg for radio buttons), `enum_name`,
`count` and `any` (for collections and counts).
Register your own with `BindingConverterRegistry.Default.Register("id", BindingConverter.Create<TIn, TOut>(...))`.

### Binding Controls

Add the `GodotHat.Binding` package to your Godot project. It needs Godot 4.4 or newer; R3.Godot isn't required. On
build it installs a small addon into `addons/godothat_binding`, because Godot only loads scripts from your own
assembly: `BindingRoot`, the node a bound scene is rooted at, and `BindingDef`, the resource each binding is stored as.
Don't edit those files; subclass `BindingRootBase` to customise the root, or set `GodotHatBindingInstallAddon` to
`false` to manage the addon yourself.

A `BindingRoot` gets its view model from, in order: its `Context` property, set by code such as a presenter or test;
an exported `ContextNode`, such as a `[ViewModel]` manager node; its exported `ViewModelType`, created through
`GodotBinding.Activator` and disposed with the root; its `SampleData` resource implementing `IViewModelSource`; or an
enclosing binding root.

Each Control's `godothat_bindings` metadata maps a target to a `BindingDef`, which you author with the editor plugin
(see below) or write in the scene directly:

```
[sub_resource type="Resource" id="Binding_volume"]
script = ExtResource("binding_def")
Path = "Settings.Volume"
Mode = 1 # TwoWay

[node name="Volume" type="HSlider" parent="Rows"]
metadata/godothat_bindings = {
&"value": SubResource("Binding_volume")
}
```

| Key                                   | Binds                                                                                  |
|---------------------------------------|----------------------------------------------------------------------------------------|
| A property, eg `text`, `value`        | The property to a value (`Kind` Property)                                              |
| A signal, eg `pressed`                | The signal to a command (`Kind` Command); availability sets a button's `disabled`      |
| A method, eg `set_text`, `grab_focus` | Calls it with each value of an observable (`Kind` Event)                               |
| `@context`                            | The data context for the Control's children; the Control's other bindings use its parent's |
| `@items`                              | The Control's children, or a list control's items, to a collection                     |
| `@selected`                           | The selected item of an `ItemList`, `OptionButton` or `Tree` bound with `@items`       |

`BindingDef` fields: `Path` (dotted, eg `Selected.Name`; empty binds the context itself), `Mode` (OneWay, TwoWay,
OneTime), `Converter` and `ConverterArg`, `Source` (Context, or ParentContext to skip the nearest context), `Parameter`
and `ParameterValue` for commands (None, the target's context Item, or a Constant), `Fallback` (applied when the path
can't be resolved; the scene's value if unset) and `Coalesce`.

Two-way bindings listen to `LineEdit`/`TextEdit.text_changed`, `Range.value_changed`, `BaseButton.toggled`,
`OptionButton.item_selected`, `TabContainer`/`TabBar.tab_changed` and `ColorPicker(Button).color_changed`; add others with
`TwoWayMap.Register`. An `OptionButton` whose `selected` is bound to an enum lists the enum's names if it has no items
of its own. Binding errors are logged with `GD.PushError` and raised as `GodotBinding.ErrorReported`, and leave the
target showing its fallback.

A view model node that leaves the tree releases the bindings to it, even if it doesn't dispose its reactive members,
though disposing them with the node is recommended.

### Editor

Enable the *GodotHat Binding* plugin (Project Settings → Plugins) to author bindings in the inspector:

- **Link buttons.** Hovering a bindable property shows a link button. Linking it binds the property: pick a path from the
  view model the scene declares with its binding root's `ViewModelType` (compatible members first, with converters
  suggested where types differ), and set the mode and converter. The scene's value stays, greyed, as the fallback.
- **Bindings section.** At the top of each Control's inspector: every binding on it, and a menu to add `@context`,
  `@items`, `@selected`, commands on signals, events and properties without a link button, such as theme overrides.
- **Bindings panel.** Lists every binding in the edited scene with its status; broken paths show red, and selecting one
  selects its node. *Preview* runs the scene's bindings in a window, with the binding root's `SampleData`.

Every edit is undoable. Properties are bindable unless hidden by rules: those not shown in the editor, a built-in
denylist of layout and editor settings, the project's `godothat_binding/rules/deny` and `allow` patterns
(`Class.property` globs, eg `Label.text` or `*.modulate`), and `[NotBindable]` / `[BindableTarget]` on your own Controls.

Declare `ViewModelType` on every binding root, including page and item template scenes: it's what the editor and
validator check paths against. A root inside another uses the context it's given when that's of its declared type, so
templates and pages bind to their item or page rather than creating their own.

### Collections

Members of any [ObservableCollections](https://github.com/Cysharp/ObservableCollections) collection
(`IObservableCollection<T>`, eg `ObservableList<T>`) or view (`ISynchronizedView<T, TView>`, from `CreateView`) can be
bound with `@items`. On a container, the `BindingDef`'s `Template` scene is instantiated for each item, with the item as
its data context, after the container's own children; bind back to the page with `Source` ParentContext, and pass the
row's item to a command with `Parameter` Item:

```
[sub_resource type="Resource" id="Binding_orders"]
script = ExtResource("binding_def")
Kind = 4 # Items
Path = "Orders"
Template = ExtResource("order_row")
```

Changes apply incrementally: adding, removing or moving items adds, removes or moves only their rows, so focus and
scroll position survive, and removed rows are pooled for reuse. Sorting, clearing, and any change to a filtered view
reset the rows, reusing the existing row for each item that remains. Replacing an item updates its row's context, so
rows can be immutable `[ViewModel]` records replaced in the collection, avoiding a reactive property per field on large
lists; mutable view models with reactive members update in place instead. There's no virtualization yet: every item
has a row.

On an `ItemList`, `OptionButton` or `Tree`, `@items` fills the control's own items instead: `ItemText` and `ItemIcon`
are paths from each item to its text and icon (empty `ItemText` shows the item itself), and `Converter` applies to the
text. Bind `@selected` two-way to a view model property of the item type.

A collection bound to an ordinary property binds its count, and the `any` converter gives whether it has items.

### Pages and services

A `ContentPresenter` shows the scene for the view model in its `@context` binding, and replaces it when that changes,
for pages, tabs and panels. Its exported `Views` dictionary maps view model type names to scenes; it falls back to
`ViewRegistry.Register<TViewModel>("res://...")`, then to its `Template`. A scene rooted at a `BindingRoot` gets the view
model as its `Context`; any other scene is bound with it as its data context. Set `OwnsContent` when the presenter
should dispose the view models it replaces, as for page navigation; leave it unset for view models that live on.

```csharp
[ViewModel]
public sealed class ShellViewModel : ViewModelBase
{
    public ShellViewModel(IViewModelActivator pages)
    {
        Page = new ReactiveProperty<object?>(pages.Create(typeof(SettingsViewModel))).AddTo(Bag);
        ShowAbout = new ReactiveCommand(_ => Page.Value = new AboutViewModel()).AddTo(Bag);
    }

    public ReactiveProperty<object?> Page { get; }  // the presenter's @context
    public ReactiveCommand ShowAbout { get; }
}
```

View models take services through their constructor. `GodotBinding.Activator`, used for `ViewModelType`, creates them
with their parameterless constructor by default; `ServiceProviderViewModelActivator` resolves constructor parameters
from any `IServiceProvider`, such as a [Jab](https://github.com/pakrym/jab) provider, through generated code with no
reflection. Register services with Jab, but not view models: Jab disposes the transients it creates, even through
factories, and view models belong to whoever creates them.

```csharp
[ServiceProvider]
[Singleton(typeof(ISettingsStore), typeof(SettingsStore))]
[Singleton(typeof(IViewModelActivator), Factory = nameof(CreateActivator))]
public partial class GameServices
{
    private IViewModelActivator CreateActivator() => new ServiceProviderViewModelActivator(this);
}

// In an autoload, before scenes with bindings load
GodotBinding.Activator = services.GetService<IViewModelActivator>();
```

Node managers created by the scene can be registered as Jab singletons with `Instance`; avoid `Factory` or constructor
registrations for nodes, which Jab would dispose. There are no DI scopes per binding root: share screen-local state by
passing it from the page view model to its sub-view models.

### Threading

Updates made on the main thread apply immediately. Updates from other threads apply on the main thread at the next
frame, so services can update view models from worker threads. A binding can also set `Coalesce` to apply only the
last value each frame, for values that change faster than they're shown.

### Testing

View models are plain C# over R3, and their services are interfaces, so most tests need no Godot at all. Test them with
any .NET test framework, with fakes for services and `FakeTimeProvider` or R3's `FakeFrameProvider` for anything time or
frame based. Keeping view models in their own project without GodotSharp, as the sample does in
[BindingSample.ViewModels](samples/BindingSample.ViewModels), keeps these tests fast and honest:

```csharp
[Fact]
public async Task StatusClearsAfterAWhile()
{
    var time = new FakeTimeProvider();
    using var vm = new SettingsViewModel(new FakeSettingsStore(), time);
    Task<string[]> messages = vm.Status.Take(2).ToArrayAsync();
    Task<string> saved = vm.Status.FirstAsync();

    vm.Save.Execute(Unit.Default);
    await saved;  // saving finishes on another thread
    time.Advance(SettingsViewModel.StatusDuration);

    Assert.Equal(["Saved 1 time(s)", ""], await messages);
}
```

`GodotHat.Binding.Testing` tests the bound scenes themselves, inside Godot with any test framework that runs there, such
as [GdUnit4](https://github.com/MikeSchulze/gdUnit4Net). `BindingHarness.Mount` adds a scene to the tree with the view
model you give it, and finds its Controls by what they bind to rather than by name, so tests survive layout changes:

```csharp
[TestCase]
public async Task SavingShowsStatusAndHistory()
{
    var store = new FakeSettingsStore();
    using var vm = new SettingsViewModel(store, TimeProvider.System);
    await using BindingHarness ui = await BindingHarness.Mount("res://Settings.tscn", vm);

    await ui.Bound<LineEdit>("PlayerName").Type("Grace");
    await ui.Bound<Button>("Save").Press();

    // Saving finishes on another thread, and its updates arrive on the main thread
    await ui.WaitUntil(() => ui.Bound<Label>("Status").Control.Text == "Saved 1 time(s)");
    ui.Bound<Button>("Save").AssertDisabled();
    ui.Items("History").AssertCount(1).Row(0).Bound<Label>("Title").AssertText("#1 Grace: Normal, volume 70%");
    ui.AssertNoBindingErrors();
}
```

- **Finding Controls.** `Bound<T>(path)` finds the one Control of that type binding the path, with a target such as
  `text` to tell apart Controls that bind it differently. `Items(path)` finds an `@items` Control, whose `Row(i)` searches
  one row; `Presenter(path)` finds a `ContentPresenter` and searches what it shows. `Find<T>("%Name")` is there for
  Controls with no binding.
- **Input.** `Press`, `SetPressed`, `Type`, `SetValue` and `Select` go through Godot's own input handling where they can,
  so two-way and command bindings are exercised, not bypassed. Each waits a frame for deferred signals such as
  `text_changed`, and fails if a player couldn't do the same, for example pressing a disabled or hidden button.
- **Assertions.** `AssertText`, `AssertVisible`, `AssertEnabled`, `AssertPressed`, `AssertValue`, `AssertSelected` and
  `AssertProperty` return the Control, so they chain. Failures throw `BindingAssertionException` with the Control, its
  path in the scene and what it binds.
- **Timing.** Updates on the main thread apply at once. `await ui.Frame()` waits for coalesced bindings, and
  `WaitUntil` for work finishing on other threads.
- **Errors.** `AssertNoBindingErrors()` fails on any binding error reported since mounting, such as a missing member or
  a failed conversion.
- **Services and sample data.** `Mount(scene, services: provider)` or `activator:` lets a scene create its own view model
  with fake services, eg from a test Jab provider, until the harness is disposed. `Mount(scene, sample: "res://...")`
  starts from an `IViewModelSource` resource, typically the scene's `SampleData`, so tests assert on the state designers
  preview.
- **Flows.** Mount a scene with presenters, act, then check what they show with `AssertShowing<TViewModel>()` and
  `AssertScene(path)`, and which view models were disposed.

Dispose the harness with `await using`, which also waits a frame for nodes queued for deletion so test frameworks don't
report orphans.

`BindingValidation.AssertProjectValid()` checks every binding in every scene against the view model types binding roots
declare, without running them, so renaming a view model member fails a one-line test instead of a playtest:

```csharp
[TestCase]
public void AllBindingsAreValid() => BindingValidation.AssertProjectValid();
```

The addon also runs it from the command line for CI, exiting with 1 if any binding is broken:

```
godot --headless --path . --import
godot --headless --path . --script res://addons/godothat_binding/ValidateBindings.cs [-- --dir=res://ui --warnings-as-errors]
```

See the sample's [Tests](samples/BindingSample/Tests) for all of these, run with
`GODOT_BIN=/path/to/godot dotnet test samples/BindingSample`.

## Using godothat

### Compatibility

The source generators (`GodotHat.SourceGenerators`, and the `GodotHat.Attributes` they use) have no package
dependency on Godot, so they don't tie your project to a specific Godot version. Instead, the code they generate is
compiled into your project against whichever GodotSharp you use. That code uses GodotSharp's script bridge APIs (the
same ones Godot's own source generators use), so it needs a compatible Godot:

- **Godot 4.4 or newer** with C# (GodotSharp). The test suite passes against GodotSharp 4.3 through 4.8-dev, and
  is primarily developed against 4.7.2.
- Godot 4.3 also works, but its projects default to `net6.0`, so raise your project's `TargetFramework` to `net8.0`
  or newer.
- Godot 4.0 - 4.2 are not supported.
- Your project must target .NET 8 or newer.
- The new GDExtension based Godot .NET bindings (`EnableGodotDotNetPreview`) are not supported yet.

Future Godot 4.x versions will most likely work, as these bridge APIs have been stable across 4.x, but they are not
guaranteed to be; if a new Godot release breaks the generated code, please open an issue.

### Setup

1. Add the following property to your project's .csproj to disable Godot's standard ScriptMethods generator:
```
<GodotDisabledSourceGenerators>ScriptMethods</GodotDisabledSourceGenerators>
```
This is needed because source generators can't see each other's output, so Godot's generator would not register the
`_Ready`, `_EnterTree` and `_ExitTree` overrides that godothat generates. godothat's own ScriptMethods generator
replaces it for the whole project.

2. Add the `GodotHat.Attributes` and `GodotHat.SourceGenerators` nupkg deps to your project.
3. Mark your node classes (and any classes they are nested in) `partial`, add annotations and enjoy.

## Acknowledgements

### Godot Code / Inspiration

ScriptMethodsGenerator is a reimplementation of godot's ScriptMethodsGenerator to produce similar output.

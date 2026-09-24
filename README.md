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
}
```

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

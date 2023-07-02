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
  return Disposable.create(() => {
    foo.Remove(this);
    this.foo = null;
  });
}

// This will automatically create UpdateFoo and DisposeFoo methods.
public void UpdateFoo(Foo foo);
private void DisposeFoo();
```

Accessibility of the `Update` method can be controlled with the `accessibility` argument, eg
`[AutoDispose(Accessibility = Accessibility.Private)]`.

DisposeFoo will be called on tree exit.

## Other notes

Ordering of calls is by occurence within the source file, and in reverse on dispose (if applicable).
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
IDisposable? ThirdMethod() { /* ... */ }

// Generated _EnterTree is conceptually:
public override void _EnterTree()
{
  MyNodePopulatedFirst = GetNode("%MyNode");
  _disposable_FirstMethod = FirstMethod();
  SecondMethod();
  _disposable_ThirdMethod = ThirdMethod();
}

// Generated _ExitTree is conceptually:
public override void _ExitTree()
{
  _disposable_ThirdMethod?.Dispose();
  _disposable_ThirdMethod = null;
  _disposable_FirstMethod.Dispose();
  _disposable_FirstMethod = null;
}
```

## Using godothat

1. Add the following property to your project's .csproj to disable Godot's standard ScriptMethods generator:
```
<GodotDisabledSourceGenerators>ScriptMethods</GodotDisabledSourceGenerators>
```

2. Add the godohat nupkg dep to your project.
3. Add annotations and enjoy

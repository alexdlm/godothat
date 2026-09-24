#if TOOLS
namespace GodotHat.Binding.Editor;

using System.Linq;
using Godot;

// The Bindings section at the top of a Control's inspector: every binding on the node, and a menu to add context, item,
// command and event bindings, and property bindings not shown inline (eg theme overrides). The inspector rebuilds it
// after edits and assembly reloads, so its per-row handlers can be lambdas.
[Tool]
public partial class BindingsSection : VBoxContainer
{
    private const int AddContext = 0;
    private const int AddItems = 1;
    private const int AddSelected = 2;
    private const int AddEvent = 3;
    private const int AddProperty = 4;
    private const int SignalBase = 1000;

    private Control? node;
    private VBoxContainer? list;
    private string[] signals = [];
    private string? pendingKind;

    public void Setup(Control target)
    {
        this.node = target;

        var header = new HBoxContainer();
        header.AddChild(new Label { Text = "Bindings", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var add = new MenuButton { Text = "Add", Icon = EditorBindings.Icon("Add"), Flat = false };
        PopupMenu menu = add.GetPopup();
        menu.AddItem("Data context for children (@context)", AddContext);
        menu.AddItem("Items from a collection (@items)", AddItems);
        if (target is ItemList or OptionButton or Tree)
        {
            menu.AddItem("Selected item (@selected)", AddSelected);
        }

        var signalMenu = new PopupMenu { Name = "Signals" };
        using var signalList = (Godot.Collections.Array)target.GetSignalList();
        this.signals = signalList.Select(s =>
        {
            using Variant entry = s;
            using Godot.Collections.Dictionary info = entry.AsGodotDictionary();
            using Variant name = info["name"];
            return name.AsString();
        }).Order().ToArray();
        for (int i = 0; i < this.signals.Length; i++)
        {
            signalMenu.AddItem(this.signals[i], SignalBase + i);
        }

        signalMenu.Connect(PopupMenu.SignalName.IdPressed, new Callable(this, MethodName.OnAdd));
        menu.AddChild(signalMenu);
        menu.AddSubmenuNodeItem("Command on signal", signalMenu);
        menu.AddItem("Event calling a method…", AddEvent);
        menu.AddItem("Property…", AddProperty);
        menu.Connect(PopupMenu.SignalName.IdPressed, new Callable(this, MethodName.OnAdd));
        header.AddChild(add);
        this.AddChild(header);

        this.list = new VBoxContainer();
        this.AddChild(this.list);
        this.AddChild(new HSeparator());
        this.Refresh();
    }

    private void Refresh()
    {
        if (this.node is null || this.list is null)
        {
            return;
        }

        foreach (Node child in this.list.GetChildren())
        {
            child.QueueFree();
        }

        foreach ((string key, BindingDefBase definition) in EditorBindings.Read(this.node).OrderBy(b => b.Key))
        {
            var row = new HBoxContainer();
            var summary = new Button
            {
                Text = $"{key}  ←  {EditorBindings.Describe(definition)}",
                Flat = true,
                ClipText = true,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ToggleMode = true,
            };
            var remove = new Button { Icon = EditorBindings.Icon("Remove"), Flat = true, TooltipText = "Remove this binding" };
            remove.Pressed += () => this.OnRemove(key);
            row.AddChild(summary);
            row.AddChild(remove);
            this.list.AddChild(row);

            var editor = new BindingEditor { Visible = false };
            editor.Setup(this.node, key, KindOf(key, definition), this.TypeOf(key));
            summary.Toggled += expanded => editor.Visible = expanded;
            this.list.AddChild(editor);
        }
    }

    private void OnRemove(string key)
    {
        if (this.node is not null)
        {
            EditorBindings.Set(this.node, key, null, $"Remove binding {key}");
        }
    }

    private void OnAdd(long id)
    {
        if (this.node is null)
        {
            return;
        }

        switch (id)
        {
            case AddContext:
                this.Add(BindingRootBase.ContextKey, BindingKind.Context);
                break;
            case AddItems:
                this.Add(BindingRootBase.ItemsKey, BindingKind.Items);
                break;
            case AddSelected:
                this.Add(BindingRootBase.SelectedKey, BindingKind.Property, BindingMode.TwoWay);
                break;
            case AddEvent or AddProperty:
                this.AskForName(id == AddEvent ? "Method to call with each value" : "Property to bind", id == AddEvent ? "event" : "property");
                break;
            case >= SignalBase:
                this.Add(this.signals[id - SignalBase], BindingKind.Command);
                break;
        }
    }

    private void AskForName(string title, string kind)
    {
        this.pendingKind = kind;
        var dialog = new ConfirmationDialog { Title = title };
        var name = new LineEdit { PlaceholderText = kind == "event" ? "eg grab_focus" : "eg theme_override_colors/font_color" };
        dialog.AddChild(name);
        dialog.RegisterTextEnter(name);
        dialog.Confirmed += () => this.OnNameConfirmed(name);
        dialog.Connect(AcceptDialog.SignalName.Confirmed, new Callable(dialog, Node.MethodName.QueueFree));
        dialog.Connect(AcceptDialog.SignalName.Canceled, new Callable(dialog, Node.MethodName.QueueFree));
        EditorInterface.Singleton.GetBaseControl().AddChild(dialog);
        dialog.PopupCentered(new Vector2I(420, 0));
        name.GrabFocus();
    }

    private void OnNameConfirmed(LineEdit name)
    {
        string key = name.Text.Trim();
        if (key.Length > 0)
        {
            this.Add(key, this.pendingKind == "event" ? BindingKind.Event : BindingKind.Property);
        }
    }

    private void Add(string key, BindingKind kind, BindingMode mode = BindingMode.OneWay)
    {
        if (this.node is null || EditorBindings.TryGet(this.node, key, out _))
        {
            return;
        }

        EditorBindings.Set(this.node, key, new BindingDef { Kind = kind, Mode = mode }, $"Add binding {key}");
    }

    private TargetTypeInfo TypeOf(string key) =>
        this.node is not null && GodotTargets.TryGetProperty(this.node, key, out TargetPropertyInfo property) ? GodotTargets.TypeOf(property) : default;

    private static BindingKind KindOf(string key, BindingDefBase definition) => key switch
    {
        BindingRootBase.ContextKey => BindingKind.Context,
        BindingRootBase.ItemsKey => BindingKind.Items,
        _ => definition.Kind,
    };
}
#endif

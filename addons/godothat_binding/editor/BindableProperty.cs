#if TOOLS
namespace GodotHat.Binding.Editor;

using Godot;

// A property row with a link button. Unbound, it shows the stock editor; bound, it shows the binding, with the scene's
// value greyed out (it's used as the fallback), and the binding's settings below when expanded.
[Tool]
public partial class BindableProperty : EditorProperty
{
    private Control? target;
    private string property = "";
    private EditorProperty? inner;
    private TargetTypeInfo type;
    private Button? link;
    private Button? summary;
    private BindingEditor? editor;
    private bool hovered;

    public void Setup(Control target, string property, EditorProperty inner, TargetTypeInfo type)
    {
        this.target = target;
        this.property = property;
        this.inner = inner;
        this.type = type;

        inner.SetObjectAndProperty(target, property);
        inner.DrawLabel = false;
        inner.Selectable = false;
        inner.Set("draw_background", false);
        inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        inner.Connect(EditorProperty.SignalName.PropertyChanged, new Callable(this, MethodName.OnInnerChanged));

        this.summary = new Button { Flat = true, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left };
        this.summary.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.OnSummaryPressed));
        this.link = new Button { Flat = true, ToggleMode = true, TooltipText = "Bind this property to the view model", Icon = EditorBindings.Icon("Unlinked") };
        this.link.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.OnLinkToggled));

        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(inner);
        row.AddChild(this.summary);
        row.AddChild(this.link);
        this.AddChild(row);

        this.Connect(Control.SignalName.MouseEntered, new Callable(this, MethodName.OnMouseEntered));
        this.Connect(Control.SignalName.MouseExited, new Callable(this, MethodName.OnMouseExited));
        this.Refresh();
    }

    public override void _UpdateProperty()
    {
        this.inner?.UpdateProperty();
        this.Refresh();
    }

    public override void _SetReadOnly(bool readOnly)
    {
        if (this.inner is not null)
        {
            this.inner.ReadOnly = readOnly;
        }
    }

    private void OnInnerChanged(StringName changedProperty, Variant value, StringName field, bool changing) =>
        this.EmitChanged(changedProperty, value, field, changing);

    private void OnMouseEntered()
    {
        this.hovered = true;
        this.Refresh();
    }

    private void OnMouseExited()
    {
        this.hovered = false;
        this.Refresh();
    }

    private void OnLinkToggled(bool on)
    {
        if (this.target is null)
        {
            return;
        }

        bool bound = EditorBindings.TryGet(this.target, this.property, out _);
        if (on && !bound)
        {
            EditorBindings.Set(this.target, this.property, new BindingDef(), $"Bind {this.property}");
            this.ShowEditor(true);
        }
        else if (!on && bound)
        {
            EditorBindings.Set(this.target, this.property, null, $"Unbind {this.property}");
            this.ShowEditor(false);
        }
    }

    private void OnSummaryPressed() => this.ShowEditor(this.editor is null);

    private void ShowEditor(bool show)
    {
        if (show && this.target is not null && this.editor is null)
        {
            this.editor = new BindingEditor();
            this.editor.Setup(this.target, this.property, BindingKind.Property, this.type);
            this.AddChild(this.editor);
            this.SetBottomEditor(this.editor);
        }
        else if (!show && this.editor is not null)
        {
            this.editor.QueueFree();
            this.editor = null;
        }
    }

    private void Refresh()
    {
        if (this.target is null || this.inner is null || this.link is null || this.summary is null)
        {
            return;
        }

        bool bound = EditorBindings.TryGet(this.target, this.property, out BindingDefBase? definition);
        this.link.SetPressedNoSignal(bound);
        this.link.Icon = EditorBindings.Icon(bound ? "Link" : "Unlinked");
        this.link.Modulate = bound || this.hovered ? Colors.White : Colors.Transparent;
        this.inner.Modulate = bound ? new Color(1, 1, 1, 0.4f) : Colors.White;
        this.summary.Visible = bound;
        this.summary.Text = definition is null ? "" : EditorBindings.Describe(definition);
        this.summary.TooltipText = "Edit the binding. The scene's value is used as the fallback.";
        this.editor?.Reload();
    }
}
#endif

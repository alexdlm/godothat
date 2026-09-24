#if TOOLS
namespace GodotHat.Binding.Editor;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

// Edits one binding: its path, with suggestions from the view model the node binds to, and the settings for its kind.
// Each change is an undoable edit of a copy of the binding.
[Tool]
public partial class BindingEditor : VBoxContainer
{
    private static readonly string[] Modes = ["One way", "Two way", "One time"];
    private static readonly string[] Parameters = ["None", "Item", "Constant"];

    private Node? node;
    private string key = "";
    private BindingKind kind;
    private TargetTypeInfo type;
    private IReadOnlyList<PathSuggestion> suggestions = [];
    private bool loading;

    private LineEdit? path;
    private MenuButton? suggest;
    private OptionButton? mode;
    private OptionButton? converter;
    private LineEdit? argument;
    private CheckBox? parentContext;
    private OptionButton? parameter;
    private LineEdit? parameterValue;
    private EditorResourcePicker? template;
    private LineEdit? itemText;
    private LineEdit? itemIcon;
    private Label? status;

    public void Setup(Node target, string bindingKey, BindingKind bindingKind, TargetTypeInfo targetType)
    {
        this.node = target;
        this.key = bindingKey;
        this.kind = bindingKind;
        this.type = targetType;

        var grid = new GridContainer { Columns = 2 };
        this.AddChild(grid);

        this.path = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "Path, eg Selected.Name" };
        this.path.Connect(LineEdit.SignalName.TextSubmitted, new Callable(this, MethodName.OnTextSubmitted));
        this.path.Connect(Control.SignalName.FocusExited, new Callable(this, MethodName.Apply));
        this.suggest = new MenuButton { Icon = EditorBindings.Icon("Search"), TooltipText = "Choose a member of the view model" };
        this.suggest.Connect(MenuButton.SignalName.AboutToPopup, new Callable(this, MethodName.FillSuggestions));
        this.suggest.GetPopup().Connect(PopupMenu.SignalName.IdPressed, new Callable(this, MethodName.OnSuggestion));
        var pathRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pathRow.AddChild(this.path);
        pathRow.AddChild(this.suggest);
        AddRow(grid, "Path", pathRow);

        if (bindingKind is BindingKind.Property or BindingKind.Event)
        {
            if (bindingKind == BindingKind.Property)
            {
                this.mode = Options(Modes);
                AddRow(grid, "Mode", this.mode);
            }

            this.converter = Options(["(none)", .. GodotBinding.Converters.Ids.Order()]);
            AddRow(grid, "Converter", this.converter);
            this.argument = this.Text("Converter argument, eg {0:N0}");
            AddRow(grid, "Argument", this.argument);
        }

        if (bindingKind == BindingKind.Command)
        {
            this.parameter = Options(Parameters);
            AddRow(grid, "Parameter", this.parameter);
            this.parameterValue = this.Text("Constant parameter");
            AddRow(grid, "Value", this.parameterValue);
        }

        if (bindingKind == BindingKind.Items)
        {
            if (target is ItemList or OptionButton or Tree)
            {
                this.itemText = this.Text("Path from each item to its text");
                AddRow(grid, "Item text", this.itemText);
                this.itemIcon = this.Text("Path from each item to its icon");
                AddRow(grid, "Item icon", this.itemIcon);
            }
            else
            {
                this.template = new EditorResourcePicker { BaseType = "PackedScene", SizeFlagsHorizontal = SizeFlags.ExpandFill };
                this.template.Connect(EditorResourcePicker.SignalName.ResourceChanged, new Callable(this, MethodName.OnTemplateChanged));
                AddRow(grid, "Template", this.template);
            }
        }

        this.parentContext = new CheckBox { Text = "From the parent context" };
        this.parentContext.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.OnToggled));
        AddRow(grid, "", this.parentContext);

        var footer = new HBoxContainer();
        this.status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var inspect = new Button { Text = "All settings", TooltipText = "Edit the BindingDef resource, including its fallback" };
        inspect.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.OnInspect));
        footer.AddChild(this.status);
        footer.AddChild(inspect);
        this.AddChild(footer);

        foreach (OptionButton? options in new[] { this.mode, this.converter, this.parameter })
        {
            options?.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.OnSelected));
        }

        this.Reload();
    }

    // Shows the node's current binding.
    public void Reload()
    {
        if (this.node is null || !GodotObject.IsInstanceValid(this.node) || !EditorBindings.TryGet(this.node, this.key, out BindingDefBase? definition) || definition is null)
        {
            return;
        }

        this.loading = true;
        SetText(this.path, definition.Path);
        this.mode?.Select((int)definition.Mode);
        this.converter?.Select(Enumerable.Range(0, this.converter.ItemCount).FirstOrDefault(i => this.converter.GetItemText(i) == definition.Converter.ToString()));
        SetText(this.argument, definition.ConverterArg);
        this.parameter?.Select((int)definition.Parameter);
        SetText(this.parameterValue, definition.ParameterValue);
        SetText(this.itemText, definition.ItemText);
        SetText(this.itemIcon, definition.ItemIcon);
        if (this.template is not null)
        {
            this.template.EditedResource = definition.Template;
        }

        this.parentContext?.SetPressedNoSignal(definition.Source == BindingSourceKind.ParentContext);
        this.loading = false;
        this.ShowStatus();
    }

    private void Apply()
    {
        if (this.loading || this.node is null || !EditorBindings.TryGet(this.node, this.key, out BindingDefBase? current))
        {
            return;
        }

        BindingDef next = EditorBindings.Copy(current);
        next.Kind = this.kind;
        next.Path = this.path?.Text.Trim() ?? "";
        next.Mode = this.mode is null ? next.Mode : (BindingMode)this.mode.Selected;
        next.Converter = this.converter is null ? next.Converter : this.converter.Selected <= 0 ? new StringName() : new StringName(this.converter.GetItemText(this.converter.Selected));
        next.ConverterArg = this.argument?.Text ?? next.ConverterArg;
        next.Parameter = this.parameter is null ? next.Parameter : (BindingParameterKind)this.parameter.Selected;
        next.ParameterValue = this.parameterValue?.Text ?? next.ParameterValue;
        next.ItemText = this.itemText?.Text ?? next.ItemText;
        next.ItemIcon = this.itemIcon?.Text ?? next.ItemIcon;
        next.Template = this.template is null ? next.Template : this.template.EditedResource as PackedScene;
        next.Source = this.parentContext?.ButtonPressed == true ? BindingSourceKind.ParentContext : BindingSourceKind.Context;

        if (!Same(current!, next))
        {
            EditorBindings.Set(this.node, this.key, next, $"Edit binding {this.key}");
        }

        this.ShowStatus();
    }

    private void OnTextSubmitted(string text) => this.Apply();

    private void OnSelected(long index) => this.Apply();

    private void OnToggled(bool pressed) => this.Apply();

    private void OnTemplateChanged(Resource resource) => this.Apply();

    private void OnInspect()
    {
        if (this.node is not null && EditorBindings.TryGet(this.node, this.key, out BindingDefBase? definition) && definition is not null)
        {
            EditorInterface.Singleton.EditResource(definition);
        }
    }

    // Lists the view model's members, compatible ones first; incompatible ones are disabled.
    private void FillSuggestions()
    {
        PopupMenu popup = this.suggest!.GetPopup();
        popup.Clear();
        Type? context = this.ContextType();
        if (context is null)
        {
            popup.AddItem("Set ViewModelType on the BindingRoot to list its members");
            popup.SetItemDisabled(0, true);
            return;
        }

        this.suggestions = PathSuggester.Suggest(context, this.kind, this.type).Take(200).ToList();
        for (int i = 0; i < this.suggestions.Count; i++)
        {
            PathSuggestion suggestion = this.suggestions[i];
            string hint = suggestion.Compatibility == PathCompatibility.NeedsConverter ? $"   via {suggestion.Converters[0]}" : "";
            popup.AddItem($"{suggestion.Path}   ({suggestion.Member.ValueType.Name}){hint}", i);
            popup.SetItemDisabled(popup.ItemCount - 1, suggestion.Compatibility == PathCompatibility.Incompatible);
        }
    }

    private void OnSuggestion(long id)
    {
        PathSuggestion suggestion = this.suggestions[(int)id];
        this.path!.Text = suggestion.Path;
        if (suggestion.Compatibility == PathCompatibility.NeedsConverter && this.converter is { Selected: <= 0 })
        {
            this.converter.Select(Enumerable.Range(0, this.converter.ItemCount).FirstOrDefault(i => this.converter.GetItemText(i) == suggestion.Converters[0]));
        }

        this.Apply();
    }

    private Type? ContextType()
    {
        if (this.node is null)
        {
            return null;
        }

        bool fromParent = this.parentContext?.ButtonPressed == true;
        return BindingValidator.ContextTypeAt(fromParent && this.node.GetParent() is { } parent ? parent : this.node);
    }

    private void ShowStatus()
    {
        if (this.status is null || this.node is null)
        {
            return;
        }

        BindingReport? report = BindingValidator.Validate(this.node, "", null, BindingValidator.ContextTypeAt(this.node))
            .FirstOrDefault(r => r.NodePath == "." && r.Key == this.key);
        this.status.Text = report is null || report.Status == BindingStatus.Ok ? "" : report.Message;
        this.status.Modulate = report?.Status == BindingStatus.Error ? new Color(1, 0.4f, 0.4f) : new Color(1, 1, 1, 0.7f);
    }

    private static bool Same(BindingDefBase a, BindingDefBase b) =>
        a.Kind == b.Kind && a.Path == b.Path && a.Mode == b.Mode && a.Converter == b.Converter && a.ConverterArg == b.ConverterArg &&
        a.Source == b.Source && a.Parameter == b.Parameter && a.ParameterValue == b.ParameterValue && a.Template == b.Template &&
        a.ItemText == b.ItemText && a.ItemIcon == b.ItemIcon;

    private LineEdit Text(string placeholder)
    {
        var edit = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = placeholder };
        edit.Connect(LineEdit.SignalName.TextSubmitted, new Callable(this, MethodName.OnTextSubmitted));
        edit.Connect(Control.SignalName.FocusExited, new Callable(this, MethodName.Apply));
        return edit;
    }

    private static OptionButton Options(IEnumerable<string> items)
    {
        var options = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string item in items)
        {
            options.AddItem(item);
        }

        return options;
    }

    private static void AddRow(GridContainer grid, string label, Control control)
    {
        grid.AddChild(new Label { Text = label });
        grid.AddChild(control);
    }

    private static void SetText(LineEdit? edit, string text)
    {
        if (edit is not null && edit.Text != text)
        {
            edit.Text = text;
        }
    }
}
#endif

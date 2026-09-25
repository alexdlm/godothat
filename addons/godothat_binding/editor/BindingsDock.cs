#if TOOLS
namespace GodotHat.Binding.Editor;

using System.Collections.Generic;
using Godot;

// A bottom panel listing every binding in the edited scene with its status, from BindingValidator. Selecting a row
// selects its node. Preview opens the scene with its bindings live, using the binding roots' sample data.
[Tool]
public partial class BindingsDock : VBoxContainer
{
    private static readonly Color ErrorColor = new(1, 0.45f, 0.45f);
    private static readonly Color WarningColor = new(1, 0.8f, 0.4f);
    private static readonly Color UncheckedColor = new(1, 1, 1, 0.5f);

    private Tree? tree;
    private CheckBox? problemsOnly;
    private Label? summary;

    public override void _EnterTree()
    {
        RegisterProjectSettings();
        if (this.tree is not null)
        {
            return;
        }

        var toolbar = new HBoxContainer();
        var refresh = new Button { Text = "Revalidate", Icon = EditorBindings.Icon("Reload") };
        refresh.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.Refresh));
        this.problemsOnly = new CheckBox { Text = "Problems only" };
        this.problemsOnly.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.OnProblemsOnly));
        var preview = new Button { Text = "Preview", Icon = EditorBindings.Icon("Play"), TooltipText = "Run the scene's bindings with its binding roots' sample data" };
        preview.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.Preview));
        this.summary = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
        toolbar.AddChild(refresh);
        toolbar.AddChild(this.problemsOnly);
        toolbar.AddChild(preview);
        toolbar.AddChild(this.summary);
        this.AddChild(toolbar);

        this.tree = new Tree { Columns = 4, HideRoot = true, ColumnTitlesVisible = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
        string[] titles = ["Node", "Target", "Path", "Status"];
        for (int i = 0; i < titles.Length; i++)
        {
            this.tree.SetColumnTitle(i, titles[i]);
            this.tree.SetColumnExpand(i, true);
        }

        this.tree.Connect(Tree.SignalName.ItemSelected, new Callable(this, MethodName.OnItemSelected));
        this.AddChild(this.tree);

        EditorInterface.Singleton.GetInspector().Connect(EditorInspector.SignalName.PropertyEdited, new Callable(this, MethodName.OnPropertyEdited));
    }

    public override void _ExitTree()
    {
        EditorInspector inspector = EditorInterface.Singleton.GetInspector();
        var callable = new Callable(this, MethodName.OnPropertyEdited);
        if (inspector.IsConnected(EditorInspector.SignalName.PropertyEdited, callable))
        {
            inspector.Disconnect(EditorInspector.SignalName.PropertyEdited, callable);
        }
    }

    // Called by the plugin when the edited scene changes, and after edits.
    public void Refresh()
    {
        if (this.tree is null)
        {
            return;
        }

        this.tree.Clear();
        TreeItem root = this.tree.CreateItem();
        Node? scene = EditorInterface.Singleton.GetEditedSceneRoot();
        if (scene is null)
        {
            this.summary!.Text = "";
            return;
        }

        IReadOnlyList<BindingReport> reports = BindingValidator.Validate(scene, scene.SceneFilePath);
        int errors = 0;
        int warnings = 0;
        foreach (BindingReport report in reports)
        {
            errors += report.Status == BindingStatus.Error ? 1 : 0;
            warnings += report.Status == BindingStatus.Warning ? 1 : 0;
            if (this.problemsOnly!.ButtonPressed && report.Status is BindingStatus.Ok or BindingStatus.Unchecked)
            {
                continue;
            }

            TreeItem item = this.tree.CreateItem(root);
            string template = report.Scene.Length > 0 && report.Scene != scene.SceneFilePath ? $" ({report.Scene.GetFile()})" : "";
            item.SetText(0, report.NodePath + template);
            item.SetText(1, report.Key);
            item.SetText(2, report.Path);
            item.SetText(3, report.Status == BindingStatus.Ok ? "OK" : report.Message.Length > 0 ? report.Message : report.Status.ToString());
            item.SetTooltipText(3, report.Message);
            item.SetMetadata(0, report.Scene == scene.SceneFilePath || report.Scene.Length == 0 ? report.NodePath : "");
            Color? color = report.Status switch
            {
                BindingStatus.Error => ErrorColor,
                BindingStatus.Warning => WarningColor,
                BindingStatus.Unchecked => UncheckedColor,
                _ => null,
            };
            if (color is { } c)
            {
                for (int column = 0; column < 4; column++)
                {
                    item.SetCustomColor(column, c);
                }
            }
        }

        this.summary!.Text = $"{reports.Count} bindings, {errors} errors, {warnings} warnings";
    }

    private void OnProblemsOnly(bool pressed) => this.Refresh();

    private void OnPropertyEdited(string property) => this.Refresh();

    private void OnItemSelected()
    {
        if (this.tree?.GetSelected() is not { } item || EditorInterface.Singleton.GetEditedSceneRoot() is not { } scene ||
            item.GetMetadata(0).AsString() is not { Length: > 0 } path || scene.GetNodeOrNull(path) is not { } node)
        {
            return;
        }

        EditorSelection selection = EditorInterface.Singleton.GetSelection();
        selection.Clear();
        selection.AddNode(node);
        EditorInterface.Singleton.EditNode(node);
    }

    // Instantiates the edited scene in a window, under a node marked as a design-time preview so its binding roots bind.
    private void Preview()
    {
        if (EditorInterface.Singleton.GetEditedSceneRoot() is not { } scene)
        {
            return;
        }

        var packed = new PackedScene();
        if (packed.Pack(scene) != Error.Ok)
        {
            return;
        }

        var window = new Window { Title = $"Binding preview: {scene.Name}", Size = new Vector2I(960, 600), Transient = true, Exclusive = false };
        var holder = new PanelContainer { AnchorRight = 1, AnchorBottom = 1 };
        holder.SetMeta(BindingRootBase.DesignTimePreviewMetaKey, true);
        window.AddChild(holder);
        window.Connect(Window.SignalName.CloseRequested, new Callable(window, Node.MethodName.QueueFree));
        EditorInterface.Singleton.GetBaseControl().AddChild(window);
        holder.AddChild(packed.Instantiate());
        window.PopupCentered();
    }

    private static void RegisterProjectSettings()
    {
        foreach (string setting in new[] { GodotTargets.DenySetting, GodotTargets.AllowSetting })
        {
            if (!ProjectSettings.HasSetting(setting))
            {
                ProjectSettings.SetSetting(setting, new string[0]);
            }

            ProjectSettings.SetInitialValue(setting, new string[0]);
            ProjectSettings.AddPropertyInfo(new Godot.Collections.Dictionary
            {
                ["name"] = setting,
                ["type"] = (int)Variant.Type.PackedStringArray,
            });
        }
    }
}
#endif

using System.Text;
using Godot;
using Range = Godot.Range;

namespace GodotHat.Binding.Testing;

/// <summary>
/// A Control found in a mounted scene, with input helpers that act as a player would and assertions on what it shows.
/// </summary>
/// <remarks>
/// Input goes through Godot's own input handling where it can (focus and <c>ui_accept</c> to press, key events to type),
/// so disabled and read-only Controls behave as they do in the game, and each helper waits a frame afterwards for
/// deferred signals such as <c>text_changed</c>. Helpers fail rather than silently doing nothing when a player
/// couldn't do the same, eg pressing a disabled or hidden button.
/// </remarks>
/// <typeparam name="T">The Control's type.</typeparam>
public sealed class BoundControl<T>
    where T : Control
{
    private static readonly StringName UiAccept = "ui_accept";
    private static readonly StringName TextProperty = "text";

    private readonly string description;

    internal BoundControl(T control, string description)
    {
        this.Control = control;
        this.description = description;
    }

    /// <summary>The Control.</summary>
    public T Control { get; }

    /// <summary>The Control.</summary>
    public static implicit operator T(BoundControl<T> bound) => bound.Control;

    /// <summary>Presses a button: <c>pressed</c>, and <c>toggled</c> for toggle buttons, are emitted as for a click.</summary>
    /// <exception cref="BindingAssertionException">It isn't a button, or it is hidden or disabled.</exception>
    public async Task Press()
    {
        BaseButton button = this.As<BaseButton>("pressed");
        this.RequireVisible("pressed");
        if (button.Disabled)
        {
            throw this.Fail("is disabled, so it can't be pressed");
        }

        this.WithFocus(button, "pressed", () =>
        {
            Viewport viewport = button.GetViewport();
            using var down = new InputEventAction { Action = UiAccept, Pressed = true };
            viewport.PushInput(down);
            using var up = new InputEventAction { Action = UiAccept, Pressed = false };
            viewport.PushInput(up);
        });
        await Frames.Next();
    }

    /// <summary>Presses a toggle button, such as a CheckBox, if it isn't already <paramref name="pressed"/>.</summary>
    /// <exception cref="BindingAssertionException">It isn't a toggle button, or it is hidden or disabled.</exception>
    public async Task SetPressed(bool pressed = true)
    {
        BaseButton button = this.As<BaseButton>("toggled");
        if (!button.ToggleMode)
        {
            throw this.Fail("isn't a toggle button");
        }

        if (button.ButtonPressed != pressed)
        {
            await this.Press();
        }
    }

    /// <summary>
    /// Replaces the text of a LineEdit, TextEdit or SpinBox by typing <paramref name="text"/>, one key at a time, so length
    /// limits and filters apply. <c>\n</c> types Enter.
    /// </summary>
    /// <param name="text">The text to type; empty clears it.</param>
    /// <param name="submit">Whether to press Enter afterwards, emitting a LineEdit's <c>text_submitted</c>. A SpinBox always submits, to apply the value.</param>
    /// <exception cref="BindingAssertionException">It isn't editable text, or it is hidden or read-only.</exception>
    public async Task Type(string text, bool submit = false)
    {
        this.RequireVisible("typed into");
        (Control editor, bool editable) = this.Control switch
        {
            LineEdit line => ((Control)line, line.Editable),
            TextEdit edit => (edit, edit.Editable),
            SpinBox spin => (spin.GetLineEdit(), spin.Editable),
            _ => throw this.Fail("isn't a LineEdit, TextEdit or SpinBox, so it can't be typed into"),
        };

        if (!editable)
        {
            throw this.Fail("is read-only, so it can't be typed into");
        }

        if (submit && editor is TextEdit)
        {
            throw this.Fail("is a TextEdit, which has no submit");
        }

        this.WithFocus(editor, "typed into", () =>
        {
            switch (editor)
            {
                case LineEdit line:
                    if (!line.IsEditing())
                    {
                        line.Edit();
                    }

                    SelectAll(line.SelectingEnabled, value => line.SelectingEnabled = value, line.SelectAll);
                    break;
                case TextEdit edit:
                    SelectAll(edit.SelectingEnabled, value => edit.SelectingEnabled = value, edit.SelectAll);
                    break;
            }

            Viewport viewport = editor.GetViewport();
            if (text.Length == 0)
            {
                PushKey(viewport, Key.Backspace);
            }

            foreach (Rune rune in text.EnumerateRunes())
            {
                if (rune.Value == '\n')
                {
                    PushKey(viewport, Key.Enter);
                }
                else
                {
                    PushKey(viewport, Key.None, rune.Value);
                }
            }

            if (submit || this.Control is SpinBox)
            {
                PushKey(viewport, Key.Enter);
            }
        });
        await Frames.Next();
    }

    /// <summary>Sets the value of a Range, such as a slider or SpinBox, emitting <c>value_changed</c> as dragging it would.</summary>
    /// <exception cref="BindingAssertionException">It isn't a Range, or it is hidden or read-only.</exception>
    public async Task SetValue(double value)
    {
        Range range = this.As<Range>("set to a value");
        this.RequireVisible("changed");
        if (range is Slider { Editable: false } or SpinBox { Editable: false })
        {
            throw this.Fail("is read-only, so it can't be changed");
        }

        range.Value = value;
        await Frames.Next();
    }

    /// <summary>
    /// Selects the item at <paramref name="index"/> of an OptionButton, ItemList, Tree (its top-level items), TabBar or
    /// TabContainer, emitting its selection signal as a click would.
    /// </summary>
    /// <exception cref="BindingAssertionException">It can't select items, or the item doesn't exist or is disabled.</exception>
    public async Task Select(int index)
    {
        this.RequireVisible("selected from");
        IReadOnlyList<string> items = this.ItemTexts();
        if (index < 0 || index >= items.Count)
        {
            throw this.Fail($"has {items.Count} items, so there's no item {index}");
        }

        switch (this.Control)
        {
            case OptionButton option:
                if (option.Disabled || option.IsItemDisabled(index) || option.IsItemSeparator(index))
                {
                    throw this.Fail(option.Disabled ? "is disabled" : $"can't select item {index} '{items[index]}'");
                }

                option.GetPopup().EmitSignal(PopupMenu.SignalName.IndexPressed, index);
                break;
            case ItemList list:
                if (list.IsItemDisabled(index) || !list.IsItemSelectable(index))
                {
                    throw this.Fail($"can't select item {index} '{items[index]}'");
                }

                if (list.SelectMode == ItemList.SelectModeEnum.Multi)
                {
                    list.Select(index, single: false);
                    list.EmitSignal(ItemList.SignalName.MultiSelected, index, true);
                }
                else
                {
                    list.Select(index);
                    list.EmitSignal(ItemList.SignalName.ItemSelected, index);
                }

                break;
            case Tree tree:
                TopLevelItems(tree)[index].Select(0);
                break;
            case TabBar bar:
                RequireTabEnabled(bar.IsTabDisabled(index));
                bar.CurrentTab = index;
                break;
            case TabContainer tabs:
                RequireTabEnabled(tabs.IsTabDisabled(index));
                tabs.CurrentTab = index;
                break;
        }

        await Frames.Next();

        void RequireTabEnabled(bool disabled)
        {
            if (disabled)
            {
                throw this.Fail($"can't select disabled tab {index} '{items[index]}'");
            }
        }
    }

    /// <summary>Selects the item with the text <paramref name="text"/>.</summary>
    /// <inheritdoc cref="Select(int)"/>
    public Task Select(string text) => this.Select(this.IndexOf(text));

    /// <summary>Checks the Control's <c>text</c>.</summary>
    public BoundControl<T> AssertText(string expected)
    {
        using Variant text = this.Control.Get(TextProperty);
        if (text.VariantType is not (Variant.Type.String or Variant.Type.StringName))
        {
            throw this.Fail("has no text");
        }

        return this.Check(text.AsString() == expected, $"text '{expected}'", $"'{text.AsString()}'");
    }

    /// <summary>Checks that the Control is visible: shown, along with all its parents.</summary>
    public BoundControl<T> AssertVisible() => this.Check(this.Control.IsVisibleInTree(), "to be visible", "hidden");

    /// <summary>Checks that the Control, or one of its parents, is hidden.</summary>
    public BoundControl<T> AssertHidden() => this.Check(!this.Control.IsVisibleInTree(), "to be hidden", "visible");

    /// <summary>Checks that a button isn't disabled, or that text, a slider or a SpinBox is editable.</summary>
    public BoundControl<T> AssertEnabled() => this.Check(this.IsEnabled(), "to be enabled", "disabled");

    /// <summary>Checks that a button is disabled, or that text, a slider or a SpinBox is read-only.</summary>
    public BoundControl<T> AssertDisabled() => this.Check(!this.IsEnabled(), "to be disabled", "enabled");

    /// <summary>Checks whether a toggle button, such as a CheckBox, is pressed.</summary>
    public BoundControl<T> AssertPressed(bool expected = true)
    {
        bool pressed = this.As<BaseButton>("checked for being pressed").ButtonPressed;
        return this.Check(pressed == expected, expected ? "to be pressed" : "not to be pressed", pressed ? "pressed" : "not pressed");
    }

    /// <summary>Checks the value of a Range, such as a slider or SpinBox.</summary>
    public BoundControl<T> AssertValue(double expected)
    {
        double value = this.As<Range>("checked for a value").Value;
        return this.Check(Mathf.IsEqualApprox(value, expected), $"value {expected}", $"{value}");
    }

    /// <summary>Checks which item is selected, by index, or -1 for none.</summary>
    public BoundControl<T> AssertSelected(int expected)
    {
        int selected = this.SelectedIndex();
        return this.Check(selected == expected, $"item {expected} selected", selected < 0 ? "none" : $"item {selected}");
    }

    /// <summary>Checks the text of the selected item.</summary>
    public BoundControl<T> AssertSelected(string expected)
    {
        int selected = this.SelectedIndex();
        string? text = selected < 0 ? null : this.ItemTexts()[selected];
        return this.Check(text == expected, $"'{expected}' selected", text is null ? "none" : $"'{text}'");
    }

    /// <summary>Checks any Godot property, eg <c>modulate</c> or <c>tooltip_text</c>.</summary>
    public BoundControl<T> AssertProperty<[MustBeVariant] TValue>(string property, TValue expected)
    {
        using Variant value = this.Control.Get(property);
        TValue actual = value.As<TValue>();
        return this.Check(EqualityComparer<TValue>.Default.Equals(actual, expected), $"{property} {expected}", $"{actual}");
    }

    internal IReadOnlyList<string> ItemTexts() => this.Control switch
    {
        OptionButton option => Enumerable.Range(0, option.ItemCount).Select(option.GetItemText).ToList(),
        ItemList list => Enumerable.Range(0, list.ItemCount).Select(list.GetItemText).ToList(),
        Tree tree => TopLevelItems(tree).Select(item => item.GetText(0)).ToList(),
        TabBar bar => Enumerable.Range(0, bar.TabCount).Select(bar.GetTabTitle).ToList(),
        TabContainer tabs => Enumerable.Range(0, tabs.GetTabCount()).Select(tabs.GetTabTitle).ToList(),
        _ => throw this.Fail("has no items"),
    };

    internal int SelectedIndex() => this.Control switch
    {
        OptionButton option => option.Selected,
        ItemList list => list.GetSelectedItems() is { Length: > 0 } selected ? selected[0] : -1,
        Tree tree => tree.GetSelected() is { } item ? TopLevelItems(tree).IndexOf(item) : -1,
        TabBar bar => bar.CurrentTab,
        TabContainer tabs => tabs.CurrentTab,
        _ => throw this.Fail("has no selection"),
    };

    private static List<TreeItem> TopLevelItems(Tree tree) => tree.GetRoot()?.GetChildren().ToList() ?? [];

    private static void PushKey(Viewport viewport, Key key, int unicode = 0)
    {
        using var down = new InputEventKey { Keycode = key, Unicode = unicode, Pressed = true };
        viewport.PushInput(down);
        using var up = new InputEventKey { Keycode = key, Unicode = unicode, Pressed = false };
        viewport.PushInput(up);
    }

    // Typing replaces the selection, so select everything even if the Control doesn't let players select.
    private static void SelectAll(bool selectingEnabled, Action<bool> setSelectingEnabled, Action selectAll)
    {
        if (!selectingEnabled)
        {
            setSelectingEnabled(true);
        }

        selectAll();
        if (!selectingEnabled)
        {
            setSelectingEnabled(false);
        }
    }

    private int IndexOf(string text)
    {
        IReadOnlyList<string> items = this.ItemTexts();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == text)
            {
                return i;
            }
        }

        throw this.Fail($"has no item '{text}'; its items are {string.Join(", ", items.Select(i => $"'{i}'"))}");
    }

    // Keys and ui_accept go to the focused Control. A Control players can click but not focus is given focus for the
    // input, then focus returns to where it was, as a click wouldn't have moved it.
    private void WithFocus(Control control, string action, Action input)
    {
        Control? previous = control.GetViewport().GuiGetFocusOwner();
        Godot.Control.FocusModeEnum mode = control.FocusMode;
        if (mode == Godot.Control.FocusModeEnum.None)
        {
            control.FocusMode = Godot.Control.FocusModeEnum.Click;
        }

        control.GrabFocus();
        try
        {
            if (!control.HasFocus())
            {
                throw this.Fail($"can't take focus, so it can't be {action}; is focus disabled on a parent?");
            }

            input();
        }
        finally
        {
            if (mode == Godot.Control.FocusModeEnum.None)
            {
                control.FocusMode = mode;
                if (previous is not null && GodotObject.IsInstanceValid(previous) && previous.IsInsideTree())
                {
                    previous.GrabFocus();
                }
            }
        }
    }

    private bool IsEnabled() => this.Control switch
    {
        BaseButton button => !button.Disabled,
        LineEdit line => line.Editable,
        TextEdit edit => edit.Editable,
        SpinBox spin => spin.Editable,
        Slider slider => slider.Editable,
        _ => throw this.Fail("can't be enabled or disabled"),
    };

    private TControl As<TControl>(string action)
        where TControl : Control =>
        this.Control as TControl ?? throw this.Fail($"isn't a {typeof(TControl).Name}, so it can't be {action}");

    private void RequireVisible(string action)
    {
        if (!this.Control.IsVisibleInTree())
        {
            throw this.Fail($"is hidden, so it can't be {action}");
        }
    }

    private BoundControl<T> Check(bool ok, string expected, string actual) =>
        ok ? this : throw this.Fail($"expected {expected}, but was {actual}");

    private BindingAssertionException Fail(string message) => new($"{this.description} {message}.");
}

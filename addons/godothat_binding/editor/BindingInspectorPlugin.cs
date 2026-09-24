#if TOOLS
namespace GodotHat.Binding.Editor;

using System.Collections.Generic;
using Godot;

// Adds a Bindings section to Controls in the edited scene, and a link button to each bindable property, which keeps
// the stock property editor for unbound values.
[Tool]
public partial class BindingInspectorPlugin : EditorInspectorPlugin
{
    private GodotObject? instantiating;
    private TargetRules rules = TargetRules.Default;
    private IReadOnlyList<string> classChain = [];

    public override bool _CanHandle(GodotObject @object) =>
        @object is Control control && @object is not EditorProperty && @object != this.instantiating && EditorBindings.IsInEditedScene(control);

    public override void _ParseBegin(GodotObject @object)
    {
        this.rules = GodotTargets.ProjectRules();
        this.classChain = GodotTargets.ClassChain(@object);
        var section = new BindingsSection();
        section.Setup((Control)@object);
        this.AddCustomControl(section);
    }

    public override bool _ParseProperty(
        GodotObject @object,
        Variant.Type type,
        string name,
        PropertyHint hintType,
        string hintString,
        PropertyUsageFlags usageFlags,
        bool wide)
    {
        // The bindings metadata is edited through the section and link buttons
        if (name == "metadata/" + BindingRootBase.BindingsMetaKey)
        {
            return true;
        }

        // Checkable theme overrides show no value until overridden, so they're bound from the Bindings section instead
        if (name.StartsWith("theme_override_", System.StringComparison.Ordinal))
        {
            return false;
        }

        var property = new TargetPropertyInfo(this.classChain, name, (int)type, (int)hintType, (long)usageFlags);
        if (!this.rules.IsBindable(property, out _))
        {
            return false;
        }

        EditorProperty? inner;
        this.instantiating = @object;
        try
        {
            inner = EditorInspector.InstantiatePropertyEditor(@object, type, name, hintType, hintString, (uint)usageFlags, wide);
        }
        finally
        {
            this.instantiating = null;
        }

        if (inner is null)
        {
            return false;
        }

        var bindable = new BindableProperty();
        bindable.Setup((Control)@object, name, inner, GodotTargets.TypeOf(property));
        this.AddPropertyEditor(name, bindable);
        return true;
    }
}
#endif

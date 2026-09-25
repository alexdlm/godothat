namespace GodotHat.Binding;

using Godot;

/// <summary>
/// Checks every binding in the project and exits with 1 if any is broken, or 2 if checking fails, for CI. After
/// importing the project:
/// <code>
/// godot --headless --path . --script res://addons/godothat_binding/ValidateBindings.cs [-- --dir=res://ui --warnings-as-errors]
/// </code>
/// </summary>
public partial class ValidateBindings : SceneTree
{
    /// <inheritdoc />
    public override void _Initialize()
    {
        try
        {
            this.Quit(Validate());
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"Binding validation failed: {e}");
            this.Quit(2);
        }
    }

    private static int Validate()
    {
        string directory = "res://";
        bool warningsAsErrors = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--dir=", System.StringComparison.Ordinal))
            {
                directory = arg["--dir=".Length..];
            }
            else if (arg == "--warnings-as-errors")
            {
                warningsAsErrors = true;
            }
        }

        int errors = 0;
        int warnings = 0;
        int undeclared = 0;
        var reports = BindingValidator.ValidateProject(directory);
        foreach (BindingReport report in reports)
        {
            switch (report.Status)
            {
                case BindingStatus.Error:
                    errors++;
                    GD.PrintErr(report.ToString());
                    break;
                case BindingStatus.Warning:
                    warnings++;
                    GD.Print(report.ToString());
                    break;
                case BindingStatus.Unchecked:
                    undeclared++;
                    break;
            }
        }

        GD.Print($"Checked {reports.Count} bindings under {directory}: {errors} broken, {warnings} warnings, {undeclared} unchecked.");
        return errors > 0 || (warningsAsErrors && warnings > 0) ? 1 : 0;
    }
}

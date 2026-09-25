using System.Globalization;

namespace GodotHat.Binding;

/// <summary>A Godot <c>PROPERTY_HINT_RANGE</c> hint string, eg <c>0,100,1,or_greater</c>.</summary>
/// <param name="Min">The minimum.</param>
/// <param name="Max">The maximum.</param>
/// <param name="Step">The step, if given.</param>
/// <param name="OrGreater">Whether values above <paramref name="Max"/> are allowed.</param>
/// <param name="OrLess">Whether values below <paramref name="Min"/> are allowed.</param>
public readonly record struct RangeHint(double Min, double Max, double? Step, bool OrGreater, bool OrLess)
{
    /// <summary>Parses a range hint string; other keywords such as <c>suffix:px</c> are ignored.</summary>
    public static bool TryParse(string? hintString, out RangeHint hint)
    {
        hint = default;
        if (string.IsNullOrEmpty(hintString))
        {
            return false;
        }

        string[] parts = hintString.Split(',');
        if (parts.Length < 2 || !TryParseNumber(parts[0], out double min) || !TryParseNumber(parts[1], out double max))
        {
            return false;
        }

        double? step = parts.Length > 2 && TryParseNumber(parts[2], out double s) ? s : null;
        bool orGreater = false;
        bool orLess = false;
        foreach (string part in parts.AsSpan(2))
        {
            switch (part.Trim())
            {
                case "or_greater":
                    orGreater = true;
                    break;
                case "or_less":
                    orLess = true;
                    break;
            }
        }

        hint = new RangeHint(min, max, step, orGreater, orLess);
        return true;
    }

    /// <summary>Whether <paramref name="value"/> is within the range, allowing for <c>or_greater</c>/<c>or_less</c>.</summary>
    public bool Contains(double value) => (this.OrLess || value >= this.Min) && (this.OrGreater || value <= this.Max);

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

/// <summary>A named value from a Godot <c>PROPERTY_HINT_ENUM</c> or <c>PROPERTY_HINT_FLAGS</c> hint string.</summary>
/// <param name="Name">The option's name.</param>
/// <param name="Value">The option's value.</param>
public readonly record struct HintOption(string Name, long Value)
{
    /// <summary>
    /// Parses an enum hint string, eg <c>Zero,One,Three:3,Four</c>. Implicit values continue from the previous value.
    /// </summary>
    public static IReadOnlyList<HintOption> ParseEnum(string? hintString) => Parse(hintString, flags: false);

    /// <summary>
    /// Parses a flags hint string, eg <c>Fill,Expand,Shrink:8</c>. Implicit values are <c>1 &lt;&lt; index</c>.
    /// </summary>
    public static IReadOnlyList<HintOption> ParseFlags(string? hintString) => Parse(hintString, flags: true);

    private static List<HintOption> Parse(string? hintString, bool flags)
    {
        var options = new List<HintOption>();
        if (string.IsNullOrEmpty(hintString))
        {
            return options;
        }

        long next = flags ? 1 : 0;
        string[] parts = hintString.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            long value = flags ? 1L << i : next;
            int colon = part.LastIndexOf(':');
            if (colon >= 0 &&
                long.TryParse(part.AsSpan(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long explicitValue))
            {
                value = explicitValue;
                part = part[..colon];
            }

            options.Add(new HintOption(part, value));
            next = value + 1;
        }

        return options;
    }
}

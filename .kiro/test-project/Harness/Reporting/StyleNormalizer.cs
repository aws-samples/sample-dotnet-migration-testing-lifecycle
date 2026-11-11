using System.Globalization;
using System.Text.RegularExpressions;

namespace MigrationTests.Reporting;

/// <summary>
/// Normalizes computed-style values before Reference/Target comparison so format-only differences are not diffs:
/// colors → rgba(r,g,b,a) (hex 3/4/6/8, rgb/rgba incl. space syntax, transparent, common names; alpha 0 = transparent),
/// px numbers rounded to 0.5px, whitespace/case collapsed, keyword equivalences (normal = 400, bold = 700,
/// start = left), and "no border" (0 width or style none/hidden) equal regardless of color.
/// Pure and deterministic (unit-tested).
/// </summary>
public static class StyleNormalizer
{
    public const string Transparent = "rgba(0,0,0,0)";

    private static readonly Dictionary<string, (int R, int G, int B)> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = (0, 0, 0), ["white"] = (255, 255, 255), ["red"] = (255, 0, 0), ["green"] = (0, 128, 0),
        ["blue"] = (0, 0, 255), ["yellow"] = (255, 255, 0), ["gray"] = (128, 128, 128), ["grey"] = (128, 128, 128),
        ["silver"] = (192, 192, 192), ["maroon"] = (128, 0, 0), ["navy"] = (0, 0, 128), ["purple"] = (128, 0, 128),
        ["teal"] = (0, 128, 128), ["olive"] = (128, 128, 0), ["lime"] = (0, 255, 0), ["aqua"] = (0, 255, 255),
        ["cyan"] = (0, 255, 255), ["fuchsia"] = (255, 0, 255), ["magenta"] = (255, 0, 255), ["orange"] = (255, 165, 0),
        ["lightgray"] = (211, 211, 211), ["lightgrey"] = (211, 211, 211), ["darkgray"] = (169, 169, 169),
        ["darkgrey"] = (169, 169, 169), ["whitesmoke"] = (245, 245, 245), ["gainsboro"] = (220, 220, 220)
    };

    private static readonly Regex ColorToken = new(
        @"rgba?\([^)]*\)|#[0-9a-fA-F]{3,8}\b|\btransparent\b|\b(" + string.Join("|", Named.Keys) + @")\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PxToken = new(@"(-?\d*\.?\d+)px", RegexOptions.CultureInvariant);

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>One color value → "rgba(r,g,b,a)"; any alpha-0 color → rgba(0,0,0,0); unparsable → trimmed lower-case input.</summary>
    public static string Color(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return "";
        if (v == "transparent") return Transparent;
        double r, g, b, a = 1;
        if (Named.TryGetValue(v, out var n)) { r = n.R; g = n.G; b = n.B; }
        else if (v.StartsWith('#'))
        {
            var h = v[1..];
            if (!Regex.IsMatch(h, "^[0-9a-f]+$") || h.Length is not (3 or 4 or 6 or 8)) return v;
            if (h.Length is 3 or 4) h = string.Concat(h.Select(c => $"{c}{c}"));
            r = Convert.ToInt32(h[..2], 16); g = Convert.ToInt32(h[2..4], 16); b = Convert.ToInt32(h[4..6], 16);
            if (h.Length == 8) a = Convert.ToInt32(h[6..8], 16) / 255.0;
        }
        else if (v.StartsWith("rgb"))
        {
            var open = v.IndexOf('(');
            var close = v.LastIndexOf(')');
            if (open < 0 || close < open) return v;
            var parts = v[(open + 1)..close].Replace("/", " ").Replace(",", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return v;
            if (!TryChannel(parts[0], out r) || !TryChannel(parts[1], out g) || !TryChannel(parts[2], out b)) return v;
            if (parts.Length > 3 && !TryAlpha(parts[3], out a)) return v;
        }
        else return v;
        a = Math.Round(Math.Clamp(a, 0, 1), 2);
        if (a == 0) return Transparent;
        return $"rgba({F(Math.Round(r))},{F(Math.Round(g))},{F(Math.Round(b))},{F(a)})";
    }

    private static bool TryChannel(string s, out double v)
    {
        if (s.EndsWith('%') && double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) { v = p * 2.55; return true; }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryAlpha(string s, out double v)
    {
        if (s.EndsWith('%') && double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) { v = p / 100; return true; }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>True when the value is a color (or alpha-0) the normalizer understands.</summary>
    public static bool IsTransparent(string? value) => Color(value) == Transparent;

    /// <summary>Rounds every px number in the value to 0.5px ("12.26px 8px" → "12.5px 8px"; "-0px" → "0px").</summary>
    public static string Px(string? value) =>
        PxToken.Replace(value ?? "", m =>
        {
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return m.Value;
            var rounded = Math.Round(d * 2, MidpointRounding.AwayFromZero) / 2;
            if (rounded == 0) rounded = 0; // no "-0"
            return F(rounded) + "px";
        });

    /// <summary>Full value normalization: lower-case, colors → rgba, px → 0.5px, whitespace collapsed, ", " → ",".</summary>
    public static string Normalize(string? value)
    {
        var v = (value ?? "").Trim();
        v = ColorToken.Replace(v, m => Color(m.Value));
        v = Px(v).ToLowerInvariant();
        v = Regex.Replace(v, @"\s+", " ");
        v = Regex.Replace(v, @"\s*,\s*", ",");
        return v;
    }

    /// <summary>
    /// A border side ("1px solid rgb(0, 0, 0)" or width/style/color in any order) → normalized "1px solid rgba(0,0,0,1)",
    /// or "none" when the side draws nothing (width 0, style none/hidden, or transparent color).
    /// </summary>
    public static string Border(string? value)
    {
        var n = Normalize(value);
        if (n.Length == 0 || n == "none") return "none";
        var tokens = Regex.Matches(n, @"rgba\([^)]*\)|\S+").Select(m => m.Value).ToList();
        if (tokens.Any(t => t is "none" or "hidden")) return "none";
        if (tokens.Any(t => t == "0px" || t == "0")) return "none";
        if (tokens.Any(t => t == Transparent)) return "none";
        return n;
    }

    private static bool IsBorderProperty(string p) =>
        p == "border" || Regex.IsMatch(p, @"^border-(top|right|bottom|left)$") || p == "outline";

    private static bool IsColorProperty(string p) =>
        p.EndsWith("color", StringComparison.Ordinal) || p is "background" or "effectivebackground" or "fill" or "stroke";

    /// <summary>Normalized comparison value of a property (the base name without hover./focus./disabled./style. prefixes).</summary>
    public static string Canonical(string property, string? value)
    {
        var p = BaseProperty(property);
        if (IsBorderProperty(p)) return Border(value);
        if (IsColorProperty(p)) return Color(value);
        if (p is "font-family" or "renderedfont") // family names are not colors ("Orange Sans")
            return Regex.Replace((value ?? "").Trim().ToLowerInvariant().Replace("\"", "").Replace("'", ""), @"\s*,\s*", ",");
        var n = Normalize(value);
        switch (p)
        {
            case "font-weight":
                return n switch { "normal" => "400", "bold" => "700", _ => n };
            case "text-align":
                return n switch { "start" => "left", "end" => "right", "-webkit-left" => "left", "-webkit-right" => "right", "-webkit-center" => "center", _ => n };
            case "text-decoration":
            case "text-decoration-line":
                // "none solid rgb(..)" carries the color of a line that is not drawn.
                return n.StartsWith("none", StringComparison.Ordinal) ? "none" : n;
            case "box-shadow":
            case "text-shadow":
                return n is "" or "none" ? "none" : n;
            default:
                return n;
        }
    }

    /// <summary>"hover.background-color" → "background-color"; "style.effectiveBackground" → "effectivebackground".</summary>
    internal static string BaseProperty(string property)
    {
        var p = (property ?? "").Trim();
        var dot = p.LastIndexOf('.');
        if (dot >= 0) p = p[(dot + 1)..];
        return p.ToLowerInvariant();
    }

    /// <summary>True when the two values render the same after normalization.</summary>
    public static bool Equivalent(string property, string? reference, string? target) =>
        string.Equals(Canonical(property, reference), Canonical(property, target), StringComparison.Ordinal);
}

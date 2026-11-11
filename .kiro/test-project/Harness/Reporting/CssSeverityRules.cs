using System.Globalization;
using System.Text.RegularExpressions;
using MigrationTests.Playwright;

namespace MigrationTests.Reporting;

/// <summary>
/// Severity recalibration of style / state / mapped CSS rows (pure, deterministic). Runs after the rows are produced
/// and before acceptance. Match, layout, responsive and geometry rows are never touched (their HIGH rules stay).
/// Rows whose severity was given explicitly in css-map.json (SeverityRule already set) keep it.
/// Every recalibrated row records the rule in <see cref="CssDiffEntry.SeverityRule"/>; unparsable values keep the
/// existing severity (never downgraded on missing data).
/// </summary>
public static class CssSeverityRules
{
    private static readonly Regex PxNumber = new(@"^(-?\d*\.?\d+)px$", RegexOptions.CultureInvariant);

    public static bool Applies(CssDiffEntry d) =>
        d.Layer is CssLayers.Style or CssLayers.State or CssLayers.Mapped or ""
        && !d.Property.StartsWith("(", StringComparison.Ordinal)
        && !d.Property.StartsWith(ModuleReportGenerator.LayoutPrefix, StringComparison.Ordinal);

    public static void Apply(IEnumerable<CssDiffEntry> rows, CssSeveritySettings? s)
    {
        s ??= new CssSeveritySettings();
        foreach (var d in rows)
        {
            if (!Applies(d) || d.SeverityRule != null) continue;
            var r = Rule(d, s);
            if (r == null) continue;
            d.Severity = r.Value.Severity;
            d.SeverityRule = r.Value.Text;
            d.Actionable = !d.Accepted && d.Severity is "HIGH" or "MEDIUM";
        }
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>(severity, rule text) or null when no rule applies.</summary>
    internal static (string Severity, string Text)? Rule(CssDiffEntry d, CssSeveritySettings s)
    {
        var p = StyleNormalizer.BaseProperty(d.Property);

        if (IsTextOrBackgroundColor(p) || IsColor(p))
        {
            // WCAG contrast drop wins over the color distance.
            if (d.ContrastReference is { } cr && d.ContrastTarget is { } ct && cr >= s.ContrastMin && ct < s.ContrastMin)
                return ("HIGH", $"contrast {F(cr)}:1 -> {F(ct)}:1 below {F(s.ContrastMin)}:1 -> HIGH");
            var de = ColorMath.DeltaE(d.Reference, d.Target);
            if (de == null) return null;
            var t = $"deltaE {de.Value.ToString("0.0", CultureInfo.InvariantCulture)}";
            if (de < s.DeltaELow) return ("LOW", $"{t} < {F(s.DeltaELow)} -> LOW");
            if (de <= s.DeltaEHigh) return ("MEDIUM", $"{t} {F(s.DeltaELow)}-{F(s.DeltaEHigh)} -> MEDIUM");
            var primary = d.Interactive || (d.Role != null && s.PrimaryRoles.Contains(d.Role, StringComparer.OrdinalIgnoreCase));
            if (IsTextOrBackgroundColor(p) && primary)
                return ("HIGH", $"{t} > {F(s.DeltaEHigh)} on {(d.Interactive ? "interactive" : "primary")} element -> HIGH");
            return ("MEDIUM", $"{t} > {F(s.DeltaEHigh)} on {(IsTextOrBackgroundColor(p) ? "non-primary element" : "border/outline color")} -> MEDIUM");
        }

        if (p == "font-weight")
        {
            var a = StyleNormalizer.Canonical(p, d.Reference);
            var b = StyleNormalizer.Canonical(p, d.Target);
            foreach (var pair in s.LowFontWeightPairs ?? Array.Empty<string[]>())
                if (pair is { Length: 2 } && ((pair[0] == a && pair[1] == b) || (pair[0] == b && pair[1] == a)))
                    return ("LOW", $"font-weight {a} vs {b} -> LOW");
            return null;
        }

        double? threshold = p == "font-size" ? s.FontSizeLowPx
            : p is "line-height" or "letter-spacing" ? s.TextMetricLowPx
            : IsBox(p) ? s.BoxLowPx
            : null;
        if (threshold == null) return null;
        var diff = MaxPxDiff(p, d.Reference, d.Target);
        if (diff == null) return null;
        return diff <= threshold
            ? ("LOW", $"{p} diff {F(diff.Value)}px <= {F(threshold.Value)}px -> LOW")
            : ("MEDIUM", $"{p} diff {F(diff.Value)}px > {F(threshold.Value)}px -> MEDIUM");
    }

    private static bool IsTextOrBackgroundColor(string p) => p is "color" or "background-color" or "background" or "effectivebackground";

    private static bool IsColor(string p) => p.EndsWith("color", StringComparison.Ordinal) && p != "color" && p != "background-color";

    private static bool IsBox(string p) =>
        p == "border-radius" || p.EndsWith("-radius", StringComparison.Ordinal) && p.StartsWith("border-", StringComparison.Ordinal)
        || p.StartsWith("padding", StringComparison.Ordinal) || p.StartsWith("margin", StringComparison.Ordinal)
        || p == "border-width" || (p.StartsWith("border-", StringComparison.Ordinal) && p.EndsWith("-width", StringComparison.Ordinal));

    /// <summary>
    /// Largest per-side |diff| of two px values or px lists (1-4 values, CSS box shorthand expanded); null when a value
    /// is not plain px (normal, %, em, '/' radius syntax, ...).
    /// </summary>
    internal static double? MaxPxDiff(string property, string? reference, string? target)
    {
        var a = PxList(reference);
        var b = PxList(target);
        if (a == null || b == null) return null;
        if (a.Length != b.Length)
        {
            if (a.Length > 4 || b.Length > 4) return null;
            a = Expand(a);
            b = Expand(b);
        }
        return a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
    }

    private static double[]? PxList(string? v)
    {
        var parts = (v ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var list = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "0") { list[i] = 0; continue; }
            var m = PxNumber.Match(parts[i]);
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out list[i])) return null;
        }
        return list;
    }

    /// <summary>CSS box shorthand → top, right, bottom, left.</summary>
    private static double[] Expand(double[] v) => v.Length switch
    {
        1 => new[] { v[0], v[0], v[0], v[0] },
        2 => new[] { v[0], v[1], v[0], v[1] },
        3 => new[] { v[0], v[1], v[2], v[1] },
        _ => v
    };
}

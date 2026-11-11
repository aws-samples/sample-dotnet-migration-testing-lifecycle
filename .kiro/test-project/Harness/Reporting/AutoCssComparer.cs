using System.Globalization;
using MigrationTests.Playwright;

namespace MigrationTests.Reporting;

/// <summary>
/// CSS diff rows from the automatic element inventories of one state (pure, deterministic):
/// match (missing / extra elements, accessible-name/label/placeholder text), style (normalized computed styles of
/// auto pairs; css-map pairs already get their mapped rows), state (hover./focus./disabled.), layout (order, alignment,
/// label position, spacing, overflow, wrapping, overlap, clipping) and responsive (per extra viewport width).
/// Repeated grid cells are grouped into one row per (property, values). Acceptance and Action are applied by
/// ModuleReportGenerator afterwards (profile, then css-map).
/// </summary>
public static class AutoCssComparer
{
    public sealed class Result
    {
        public List<CssDiffEntry> Rows { get; } = new();
        public MatchResult Match { get; set; } = new();
    }

    private const int LabelMax = 40;

    /// <summary>Element label: css-map name, else "role 'name'" (name cut at 40), else "role (tag)".</summary>
    public static string Label(InventoryElement e)
    {
        if (!string.IsNullOrEmpty(e.MapName)) return e.MapName!;
        var n = e.DisplayName;
        if (n.Length > LabelMax) n = n[..LabelMax] + "...";
        return n.Length == 0 ? $"{e.Role} ({e.Tag})" : $"{e.Role} '{n}'";
    }

    public static Result Compare(string state, InventoryFile reference, InventoryFile target, StackProfile? profile,
        VisualChecksSettings? vc = null)
    {
        profile ??= new StackProfile();
        vc ??= new VisualChecksSettings();
        var tol = vc.Geometry ?? new GeometryTolerance();
        var layout = vc.Layout ?? new LayoutRelationSettings();
        // Layout-table cells (pager/toolbar tables, no header cells) are not content: left out of element matching (audit:
        // match file). Those that carry their own text are compared by that text instead (LayoutTextRows).
        var refLayout = reference.Elements.Where(e => ElementMatcher.IsLayoutCell(e, reference.Elements)).ToHashSet(ReferenceEqualityComparer.Instance);
        var tgtLayout = target.Elements.Where(e => ElementMatcher.IsLayoutCell(e, target.Elements)).ToHashSet(ReferenceEqualityComparer.Instance);
        var match = ElementMatcher.Match(reference.Elements.Where(e => !refLayout.Contains(e)).ToList(),
            target.Elements.Where(e => !tgtLayout.Contains(e)).ToList(), profile, vc.Inventory?.MinMatchScore ?? 0.6);
        var layoutText = LayoutTextCells("Reference", reference.Elements, refLayout, target.Elements)
            .Concat(LayoutTextCells("Target", target.Elements, tgtLayout, reference.Elements)).ToList();
        match.LayoutTextCells.AddRange(layoutText);
        var withText = layoutText.Select(x => x.Element).ToHashSet(ReferenceEqualityComparer.Instance);
        match.ExcludedLayoutCells.AddRange(reference.Elements.Where(e => refLayout.Contains(e) && !withText.Contains(e)).Select(e => ("Reference", e)));
        match.ExcludedLayoutCells.AddRange(target.Elements.Where(e => tgtLayout.Contains(e) && !withText.Contains(e)).Select(e => ("Target", e)));
        var rows = new List<(CssDiffEntry Row, InventoryElement Src)>();

        CssDiffEntry Add(InventoryElement src, string layer, string property, string r, string t, string severity)
        {
            var row = new CssDiffEntry
            {
                Element = Label(src), State = state, Property = property, Reference = r, Target = t,
                Severity = severity, Layer = layer, Role = src.Role, Interactive = src.Interactive
            };
            rows.Add((row, src));
            return row;
        }

        // ── match ──
        foreach (var m in match.MissingOnTarget)
            Add(m, CssLayers.Match, MissingProperty, "present", "missing", "HIGH");
        foreach (var x in match.ExtraOnTarget)
            Add(x, CssLayers.Match, ExtraProperty, "missing", "present", x.Interactive ? "MEDIUM" : "LOW");
        foreach (var p in match.Pairs.Where(p => !p.Reference.IsCell))
        {
            var labelDiff = TextRow(p.Reference.Label, p.Target.Label);
            var placeholderDiff = TextRow(p.Reference.Placeholder, p.Target.Placeholder);
            if (labelDiff != null) Add(p.Reference, CssLayers.Match, "text.label", p.Reference.Label, p.Target.Label, labelDiff);
            if (placeholderDiff != null) Add(p.Reference, CssLayers.Match, "text.placeholder", p.Reference.Placeholder, p.Target.Placeholder, placeholderDiff);
            var nameDiff = TextRow(p.Reference.Name, p.Target.Name);
            if (nameDiff != null && labelDiff == null && placeholderDiff == null)
                Add(p.Reference, CssLayers.Match, "text.name", p.Reference.Name, p.Target.Name, nameDiff);
        }

        // ── style (auto pairs) ──
        foreach (var p in match.Pairs.Where(p => p.How == "auto"))
            foreach (var (prop, rv) in p.Reference.Styles)
            {
                if (!p.Target.Styles.TryGetValue(prop, out var tv)) continue;
                if (Ignored(prop, p, profile, vc)) continue;
                if (string.Equals(rv?.Trim(), tv?.Trim(), StringComparison.Ordinal) || CssCompareHelper.IsNoiseDiff(prop, rv ?? "", tv ?? "")) continue;
                var row = Add(p.Reference, CssLayers.Style, prop, rv ?? "", tv ?? "", ModuleReportGenerator.DefaultCssSeverity(prop));
                SetPairFacts(row, p, "");
            }

        // ── interactive states (all pairs) ──
        foreach (var p in match.Pairs)
            foreach (var (key, rv) in p.Reference.States.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!p.Target.States.TryGetValue(key, out var tv)) continue;
                var baseProp = StyleNormalizer.BaseProperty(key);
                if (profile.IgnoresProperty(key) || Ignored(baseProp, p, profile, vc)) continue;
                if (string.Equals(rv?.Trim(), tv?.Trim(), StringComparison.Ordinal) || CssCompareHelper.IsNoiseDiff(baseProp, rv ?? "", tv ?? "")) continue;
                var row = Add(p.Reference, CssLayers.State, key, rv ?? "", tv ?? "", baseProp is "color" or "background-color" ? "HIGH" : "MEDIUM");
                var dot = key.IndexOf('.');
                SetPairFacts(row, p, dot > 0 ? key[..(dot + 1)] : "");
            }

        // ── layout relations ──
        LayoutRows(match, layout, tol, (src, prop, r, t, sev) => Add(src, CssLayers.Layout, prop, r, t, sev));

        // ── responsive ──
        foreach (var w in reference.Responsive.Keys.Intersect(target.Responsive.Keys)
                     .OrderBy(k => int.TryParse(k, out var n) ? n : int.MaxValue))
            ResponsiveRows(w, reference.Responsive[w], target.Responsive[w], match, (src, prop, r, t, sev) => Add(src, CssLayers.Responsive, prop, r, t, sev),
                page => rows.Add((page, new InventoryElement())), state);

        var result = new Result { Match = match };
        var targetSide = target.Elements.ToHashSet(ReferenceEqualityComparer.Instance);
        string ColumnName(InventoryElement cell) =>
            ColumnHeader(cell, targetSide.Contains(cell) ? target.Elements : reference.Elements) is { Length: > 0 } h
                ? h : $"column {(cell.ColIndex ?? 0) + 1}";
        result.Rows.AddRange(Group(rows, vc.Inventory?.MaxRowsPerGrid ?? 3, ColumnName));

        // Layout-table text not found on the other side: LOW (not counted; plain text outside compared elements is not
        // inventoried, so "not found" is a hint to check, not proof), listed so a missing caption/label is never hidden.
        var seenText = new HashSet<(string, string)>();
        foreach (var (side, _, text, _) in layoutText.Where(x => !x.Found))
        {
            var label = $"text '{(text.Length > LabelMax ? text[..LabelMax] + "..." : text)}'";
            if (!seenText.Add((side, label))) continue;
            var onRef = side == "Reference";
            result.Rows.Add(new CssDiffEntry
            {
                Element = label, State = state, Property = onRef ? LayoutTextMissingProperty : LayoutTextExtraProperty,
                Reference = onRef ? text : "not found", Target = onRef ? "not found" : text,
                Severity = "LOW", Layer = CssLayers.Match, Role = "cell"
            });
        }
        return result;
    }

    /// <summary>Property of a Reference layout-table text not found in any compared Target element (LOW, not counted).</summary>
    public const string LayoutTextMissingProperty = "(layout text not found on Target)";
    /// <summary>Property of a Target layout-table text not found in any compared Reference element (LOW, not counted).</summary>
    public const string LayoutTextExtraProperty = "(layout text only on Target)";

    /// <summary>Layout cells of one side with their own text, and whether that text appears in any element of the other side.</summary>
    private static IEnumerable<(string Side, InventoryElement Element, string Text, bool Found)> LayoutTextCells(string side,
        List<InventoryElement> own, IReadOnlySet<object?> layout, List<InventoryElement> other)
    {
        static string Key(string s) => System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        List<string>? pool = null;
        foreach (var e in own.Where(layout.Contains))
        {
            var text = ElementMatcher.OwnText(e, own);
            if (text.Length == 0) continue;
            pool ??= other.SelectMany(o => new[] { o.Name, o.Label, o.Text, o.Placeholder }).Select(Key).Where(k => k.Length > 0).Distinct().ToList();
            var key = Key(text);
            yield return (side, e, text, pool.Any(p => p.Contains(key, StringComparison.Ordinal)));
        }
    }

    /// <summary>
    /// Header text of a grid cell's column: the nearest columnheader of the same inventory above the cell whose
    /// horizontal range contains the cell's center (works for a header in its own table, e.g. jqGrid, and for thead).
    /// "" when none is found.
    /// </summary>
    internal static string ColumnHeader(InventoryElement cell, IReadOnlyList<InventoryElement> inventory)
    {
        var cx = cell.Box.CenterX;
        var header = inventory
            .Where(h => h.Role == "columnheader" && h.Box.W > 0 && h.Box.X <= cx && cx <= h.Box.Right && h.Box.Bottom <= cell.Box.Y + 2)
            .OrderByDescending(h => h.Box.Bottom).FirstOrDefault();
        if (header == null) return "";
        var n = System.Text.RegularExpressions.Regex.Replace(header.DisplayName, @"\s+", " ").Trim();
        return n.Length > LabelMax ? n[..LabelMax] + "..." : n;
    }

    /// <summary>
    /// Pair facts for the severity rules: interactive on either side, and for text/background color rows the WCAG
    /// contrast of the text color on the (effective) background per side. statePrefix "hover." / "focus." /
    /// "disabled." reads the state values first and falls back to the base styles.
    /// </summary>
    private static void SetPairFacts(CssDiffEntry row, MatchPair p, string statePrefix)
    {
        row.Interactive = p.Reference.Interactive || p.Target.Interactive;
        if (StyleNormalizer.BaseProperty(row.Property) is not ("color" or "background-color")) return;
        static string? Get(InventoryElement e, string prefix, string prop) =>
            prefix.Length > 0 && e.States.TryGetValue(prefix + prop, out var sv) ? sv
            : e.Styles.TryGetValue(prop, out var v) ? v : null;
        row.ContrastReference = Round(ColorMath.Contrast(Get(p.Reference, statePrefix, "color"), Get(p.Reference, statePrefix, "background-color")));
        row.ContrastTarget = Round(ColorMath.Contrast(Get(p.Target, statePrefix, "color"), Get(p.Target, statePrefix, "background-color")));
    }

    private static double? Round(double? v) => v == null ? null : Math.Round(v.Value, 2);

    public const string MissingProperty = "(missing on Target)";
    public const string ExtraProperty = "(extra on Target)";

    /// <summary>null = same text; LOW = only case/whitespace/trailing ':' or '*' differ; MEDIUM = other text difference.</summary>
    internal static string? TextRow(string? r, string? t)
    {
        static string Ws(string? s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();
        var a = Ws(r);
        var b = Ws(t);
        if (string.Equals(a, b, StringComparison.Ordinal)) return null;
        return ElementMatcher.NormalizeName(a) == ElementMatcher.NormalizeName(b) ? "LOW" : "MEDIUM";
    }

    /// <summary>Profile ignore list always; VisualChecks.IgnoreProperties only when neither side is interactive.</summary>
    private static bool Ignored(string prop, MatchPair p, StackProfile profile, VisualChecksSettings vc)
    {
        if (profile.IgnoresProperty(prop)) return true;
        var interactive = p.Reference.Interactive || p.Target.Interactive;
        return !interactive && (vc.IgnoreProperties ?? Array.Empty<string>()).Any(i => StackProfile.PropertyMatches(i, prop));
    }

    private static string Px(double v) => ModuleReportGenerator.Px(v);

    private delegate void RowSink(InventoryElement src, string property, string reference, string target, string severity);

    private static void LayoutRows(MatchResult match, LayoutRelationSettings layout, GeometryTolerance tol, RowSink add)
    {
        var alignTol = Math.Max(0, layout.AlignTolerancePx);

        // Per-element facts on all pairs (cells included: grouped later).
        foreach (var p in match.Pairs)
        {
            var (r, t) = (p.Reference, p.Target);
            var ro = r.OverflowX || r.Ellipsis;
            var to = t.OverflowX || t.Ellipsis;
            if (ro != to) add(r, "layout.overflow", ro ? "overflows (content cut off)" : "fits", to ? "overflows (content cut off)" : "fits", "HIGH");
            if (r.Lines > 0 && t.Lines > 0 && r.Lines != t.Lines) add(r, "layout.lines", Lines(r.Lines), Lines(t.Lines), "HIGH");
            if (r.Clipped != t.Clipped) add(r, "layout.hidden", r.Clipped ? "clipped by its container" : "fully visible", t.Clipped ? "clipped by its container" : "fully visible", "HIGH");
        }

        // Sibling relations within a Reference container (cells excluded: grid geometry covers them).
        var groups = match.Pairs.Where(p => !p.Reference.IsCell).GroupBy(p => p.Reference.Container).Where(g => g.Count() >= 2);
        foreach (var g in groups)
        {
            // Only pairs whose Target elements share one container are compared as siblings.
            var tc = g.GroupBy(p => p.Target.Container).OrderByDescending(x => x.Count()).First().Key;
            var sib = g.Where(p => p.Target.Container == tc).OrderBy(p => p.Reference.Order).ToList();
            if (sib.Count < 2) continue;
            var tRank = sib.OrderBy(p => p.Target.Order).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
            for (var i = 0; i < sib.Count; i++)
                if (tRank[sib[i]] != i)
                    add(sib[i].Reference, "layout.order", $"position {i + 1} of {sib.Count}", $"position {tRank[sib[i]] + 1} of {sib.Count}", "HIGH");

            for (var i = 1; i < sib.Count; i++)
            {
                var (prev, cur) = (sib[i - 1], sib[i]);
                var ra = Alignment(prev.Reference.Box, cur.Reference.Box, alignTol);
                var ta = Alignment(prev.Target.Box, cur.Target.Box, alignTol);
                if (ra != ta)
                    add(cur.Reference, "layout.align", AlignText(ra, prev.Reference), AlignText(ta, prev.Reference), "MEDIUM");

                var rg = Gap(prev.Reference.Box, cur.Reference.Box, alignTol);
                var tg = Gap(prev.Target.Box, cur.Target.Box, alignTol);
                if (rg != null && tg != null && rg.Value.Axis == tg.Value.Axis
                    && Math.Abs(rg.Value.Px - tg.Value.Px) > layout.SpacingMinDiffPx
                    && ModuleReportGenerator.GeometrySeverity(rg.Value.Px, tg.Value.Px, tol) != null)
                    add(cur.Reference, "layout.spacing", Px(rg.Value.Px), Px(tg.Value.Px), "MEDIUM");
            }
        }

        // Label position relative to its control.
        var byRefPath = match.Pairs.GroupBy(p => p.Reference.DomPath).ToDictionary(g => g.Key, g => g.First());
        foreach (var p in match.Pairs.Where(p => p.Reference.Role == "label" && p.Reference.LabelFor != null && p.Target.LabelFor != null))
        {
            if (!byRefPath.TryGetValue(p.Reference.LabelFor!, out var control) || control.Target.DomPath != p.Target.LabelFor) continue;
            var rp = LabelPosition(p.Reference.Box, control.Reference.Box, alignTol);
            var tp = LabelPosition(p.Target.Box, control.Target.Box, alignTol);
            if (rp != tp) add(p.Reference, "layout.labelPosition", rp, tp, "MEDIUM");
        }

        // Overlap on Target where the Reference pair does not overlap (nested elements excluded).
        var flat = match.Pairs.Where(p => !p.Reference.IsCell).ToList();
        for (var i = 0; i < flat.Count; i++)
            for (var j = i + 1; j < flat.Count; j++)
            {
                var (a, b) = (flat[i], flat[j]);
                if (Nested(a.Reference, b.Reference) || Nested(a.Target, b.Target)) continue;
                var ro = Overlap(a.Reference.Box, b.Reference.Box);
                var to = Overlap(a.Target.Box, b.Target.Box);
                if (to != null && ro == null)
                    add(a.Reference, "layout.overlap", $"no overlap with {Label(b.Reference)}",
                        $"overlaps {Label(b.Reference)} by {Px(to.Value.W)} x {Px(to.Value.H)}", "HIGH");
            }
    }

    private static string Lines(int n) => n == 1 ? "1 line" : $"{n} lines";

    [Flags]
    private enum Edges { None = 0, Left = 1, Right = 2, Top = 4 }

    private static Edges Alignment(InventoryBox a, InventoryBox b, double tol)
    {
        var e = Edges.None;
        if (Math.Abs(a.X - b.X) <= tol) e |= Edges.Left;
        if (Math.Abs(a.Right - b.Right) <= tol) e |= Edges.Right;
        if (Math.Abs(a.Y - b.Y) <= tol) e |= Edges.Top;
        return e;
    }

    private static string AlignText(Edges e, InventoryElement prev)
    {
        var what = Label(prev);
        if (e == Edges.None) return $"not aligned with {what}";
        var parts = new List<string>();
        if (e.HasFlag(Edges.Left)) parts.Add("left");
        if (e.HasFlag(Edges.Right)) parts.Add("right");
        if (e.HasFlag(Edges.Top)) parts.Add("top");
        return $"{string.Join("+", parts)} aligned with {what}";
    }

    /// <summary>Gap to the previous sibling: vertical when stacked, horizontal when side by side, null when overlapping.</summary>
    private static (char Axis, double Px)? Gap(InventoryBox prev, InventoryBox cur, double tol)
    {
        if (prev.Bottom <= cur.Y + tol) return ('v', Math.Round(cur.Y - prev.Bottom, 1));
        if (prev.Right <= cur.X + tol) return ('h', Math.Round(cur.X - prev.Right, 1));
        return null;
    }

    private static string LabelPosition(InventoryBox label, InventoryBox control, double tol)
    {
        var vOverlap = label.Y < control.Bottom && control.Y < label.Bottom;
        if (control.X >= label.Right - tol && vOverlap) return "left of its control";
        if (label.X >= control.Right - tol && vOverlap) return "right of its control";
        if (control.Y >= label.Bottom - tol) return "above its control";
        if (label.Y >= control.Bottom - tol) return "below its control";
        return "inside its control";
    }

    private static bool Nested(InventoryElement a, InventoryElement b) =>
        a.DomPath.StartsWith(b.DomPath + " >", StringComparison.Ordinal) || b.DomPath.StartsWith(a.DomPath + " >", StringComparison.Ordinal)
        || Contains(a.Box, b.Box) || Contains(b.Box, a.Box);

    private static bool Contains(InventoryBox outer, InventoryBox inner) =>
        inner.X >= outer.X - 0.5 && inner.Y >= outer.Y - 0.5 && inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static (double W, double H)? Overlap(InventoryBox a, InventoryBox b)
    {
        var w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return w > 2 && h > 2 ? (Math.Round(w, 1), Math.Round(h, 1)) : null;
    }

    private static void ResponsiveRows(string width, InventorySnapshot r, InventorySnapshot t, MatchResult match, RowSink add,
        Action<CssDiffEntry> addPage, string state)
    {
        var rByPath = r.Elements.GroupBy(e => e.DomPath).ToDictionary(g => g.Key, g => g.First());
        var tByPath = t.Elements.GroupBy(e => e.DomPath).ToDictionary(g => g.Key, g => g.First());
        var prefix = $"responsive.{width}.";
        foreach (var p in match.Pairs)
        {
            rByPath.TryGetValue(p.Reference.DomPath, out var rs);
            tByPath.TryGetValue(p.Target.DomPath, out var ts);
            if (rs == null && ts == null) continue;
            if ((rs == null) != (ts == null))
            {
                add(p.Reference, prefix + "hidden", rs != null ? "visible" : "hidden", ts != null ? "visible" : "hidden", "HIGH");
                continue;
            }
            var ro = rs!.OverflowX || rs.Ellipsis;
            var to = ts!.OverflowX || ts.Ellipsis;
            if (ro != to) add(p.Reference, prefix + "overflow", ro ? "overflows (content cut off)" : "fits", to ? "overflows (content cut off)" : "fits", "HIGH");
            if (rs.Lines > 0 && ts.Lines > 0 && rs.Lines != ts.Lines) add(p.Reference, prefix + "lines", Lines(rs.Lines), Lines(ts.Lines), "HIGH");
            if (rs.Clipped != ts.Clipped) add(p.Reference, prefix + "hidden", rs.Clipped ? "clipped by its container" : "fully visible", ts.Clipped ? "clipped by its container" : "fully visible", "HIGH");
        }
        static bool Scrolls(InventoryViewport v) => v.Width > 0 && v.ScrollWidth > v.Width + 1;
        var rsc = Scrolls(r.Viewport);
        var tsc = Scrolls(t.Viewport);
        if (rsc != tsc)
        {
            string Text(bool s, InventoryViewport v) => s
                ? $"scrolls horizontally ({v.ScrollWidth.ToString("0", CultureInfo.InvariantCulture)}px wide)"
                : $"fits {width}px";
            addPage(new CssDiffEntry
            {
                Element = "(page)", State = state, Property = prefix + "overflow", Reference = Text(rsc, r.Viewport),
                Target = Text(tsc, t.Viewport), Severity = "HIGH", Layer = CssLayers.Responsive
            });
        }
    }

    /// <summary>
    /// Grid cells: one row per (layer, role, property, values) listing the columns ("cell cells, columns 1, 3 (first 3 rows)");
    /// other rows: identical (element, property, values) collapse into the first.
    /// </summary>
    private static IEnumerable<CssDiffEntry> Group(List<(CssDiffEntry Row, InventoryElement Src)> rows, int maxRows,
        Func<InventoryElement, string>? columnName = null)
    {
        var output = new List<CssDiffEntry>();
        var cellGroups = new Dictionary<(string, string, string, string, string), (CssDiffEntry Row, SortedSet<int> Cols)>();
        var names = new Dictionary<CssDiffEntry, SortedDictionary<int, string>>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<(string, string, string, string)>();
        foreach (var (row, src) in rows)
        {
            if (src.IsCell)
            {
                var key = (row.Layer, src.Role, row.Property, row.Reference, row.Target);
                if (!cellGroups.TryGetValue(key, out var g))
                {
                    g = (row, new SortedSet<int>());
                    cellGroups[key] = g;
                    output.Add(row);
                }
                if (src.ColIndex is { } c)
                {
                    g.Cols.Add(c + 1);
                    if (columnName != null)
                    {
                        if (!names.TryGetValue(g.Row, out var byCol)) names[g.Row] = byCol = new SortedDictionary<int, string>();
                        if (!byCol.ContainsKey(c)) byCol[c] = columnName(src);
                    }
                }
                continue;
            }
            if (seen.Add((row.Element, row.Property, row.Reference, row.Target))) output.Add(row);
        }
        foreach (var ((_, role, _, _, _), (row, cols)) in cellGroups)
        {
            // Element text stays technical and stable (group keys); the column headers go to Columns for the Issue text.
            row.Element = cols.Count > 0
                ? $"{role} cells, column{(cols.Count == 1 ? "" : "s")} {string.Join(", ", cols)} (first {maxRows} rows)"
                : $"{role} cells (first {maxRows} rows)";
            if (names.TryGetValue(row, out var byCol)) row.Columns = byCol.Values.ToList();
        }
        return output;
    }
}

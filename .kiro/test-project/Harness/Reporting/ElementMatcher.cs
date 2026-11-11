using System.Text.RegularExpressions;

namespace MigrationTests.Reporting;

// ═══════════════════════════════════════════════════════════════════════
// INVENTORY FILE MODEL (runs/Module<X>/<System>/css/inventory_<state>.json, written by ElementInventory)
// ═══════════════════════════════════════════════════════════════════════

public sealed class InventoryFile
{
    public string State { get; set; } = "";
    public InventoryViewport Viewport { get; set; } = new();
    public List<InventoryElement> Elements { get; set; } = new();
    public InventoryHealth Health { get; set; } = new();
    /// <summary>Width (px, as string) → inventory at that viewport width.</summary>
    public Dictionary<string, InventorySnapshot> Responsive { get; set; } = new();
    public long CaptureMs { get; set; }
}

public sealed class InventoryViewport
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double ScrollWidth { get; set; }
    public double ScrollHeight { get; set; }
}

public sealed class InventorySnapshot
{
    public InventoryViewport Viewport { get; set; } = new();
    public List<InventoryElement> Elements { get; set; } = new();
}

public sealed class InventoryHealth
{
    public List<string> BrokenImages { get; set; } = new();
    public List<string> EmptySections { get; set; } = new();
    public List<string> A11y { get; set; } = new();
}

public sealed class InventoryBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
}

public sealed class InventoryElement
{
    /// <summary>Position in the inventory (DOM order).</summary>
    public int Index { get; set; }
    public string Role { get; set; } = "";
    /// <summary>Accessible name (aria-labelledby, aria-label, label, alt, content, title, placeholder).</summary>
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public string InputType { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Text { get; set; } = "";
    public InventoryBox Box { get; set; } = new();
    public string DomPath { get; set; } = "";
    /// <summary>DOM path of the nearest ancestor holding at least two inventoried elements.</summary>
    public string Container { get; set; } = "";
    /// <summary>Index among the inventoried elements of the same container (DOM order).</summary>
    public int Order { get; set; }
    public int? RowIndex { get; set; }
    public int? ColIndex { get; set; }
    public bool Interactive { get; set; }
    public bool Disabled { get; set; }
    /// <summary>css-map.json element name when this element is the first visible match of its selector.</summary>
    public string? MapName { get; set; }
    public Dictionary<string, string> Styles { get; set; } = new();
    /// <summary>hover.* / focus.* / disabled.* styles.</summary>
    public Dictionary<string, string> States { get; set; } = new();
    public bool OverflowX { get; set; }
    public bool Ellipsis { get; set; }
    public int Lines { get; set; }
    public bool Clipped { get; set; }
    /// <summary>Labels only: DOM path of the labelled control.</summary>
    public string? LabelFor { get; set; }

    public bool IsCell => Role is "cell" or "gridcell";

    /// <summary>Name, else label, text, placeholder.</summary>
    public string DisplayName =>
        Name.Length > 0 ? Name : Label.Length > 0 ? Label : Text.Length > 0 ? Text : Placeholder;
}

// ═══════════════════════════════════════════════════════════════════════
// MATCHER
// ═══════════════════════════════════════════════════════════════════════

public sealed class MatchPair
{
    public InventoryElement Reference { get; set; } = new();
    public InventoryElement Target { get; set; } = new();
    public double Score { get; set; }
    /// <summary>map (css-map.json pair) | auto (score).</summary>
    public string How { get; set; } = "auto";
}

public sealed class MatchResult
{
    public List<MatchPair> Pairs { get; } = new();
    public List<InventoryElement> MissingOnTarget { get; } = new();
    public List<InventoryElement> ExtraOnTarget { get; } = new();
    /// <summary>Layout-table cells left out of matching (<see cref="ElementMatcher.IsLayoutCell"/>), per side; listed in the match file.</summary>
    public List<(string Side, InventoryElement Element)> ExcludedLayoutCells { get; } = new();
    /// <summary>
    /// Layout-table cells that carry their own text (<see cref="ElementMatcher.OwnText"/>): not matched as elements, but their
    /// text is looked up on the other side; Found = false gives a LOW (not counted) row. Listed in the match file.
    /// </summary>
    public List<(string Side, InventoryElement Element, string Text, bool Found)> LayoutTextCells { get; } = new();
}

/// <summary>
/// Deterministic Reference→Target element pairing. (a) css-map.json pairs (equal mapName) always win;
/// (b) greedy best score over the rest, highest first, ties by Reference then Target DOM order. Score (0..1):
/// role equal or profile-equivalent is required (else 0); accessible name/label/text: normalized equality 0.6,
/// else token Jaccard ≥ 0.6 gives 0.6 × similarity, both empty 0.3; same relative order in the container
/// (grid cells: same row and column) 0.25, off by one 0.12; geometry proximity 0.15 × (1 − min(1, center distance / 400px)).
/// A pair needs score ≥ MinMatchScore. Elements whose role the profile ignores are left out.
/// </summary>
public static class ElementMatcher
{
    public const double NameWeight = 0.6;
    public const double OrderWeight = 0.25;
    public const double GeometryWeight = 0.15;
    public const double ProximityPx = 400;

    public static MatchResult Match(IReadOnlyList<InventoryElement> reference, IReadOnlyList<InventoryElement> target,
        StackProfile? profile = null, double minScore = 0.6)
    {
        profile ??= new StackProfile();
        var refs = reference.Where(e => !profile.IgnoresRole(e.Role)).ToList();
        var tgts = target.Where(e => !profile.IgnoresRole(e.Role)).ToList();
        var result = new MatchResult();
        var usedR = new HashSet<InventoryElement>(ReferenceEqualityComparer.Instance);
        var usedT = new HashSet<InventoryElement>(ReferenceEqualityComparer.Instance);

        foreach (var r in refs.Where(e => !string.IsNullOrEmpty(e.MapName)))
        {
            var t = tgts.FirstOrDefault(x => !usedT.Contains(x) && string.Equals(x.MapName, r.MapName, StringComparison.Ordinal));
            if (t == null) continue;
            result.Pairs.Add(new MatchPair { Reference = r, Target = t, Score = 1, How = "map" });
            usedR.Add(r); usedT.Add(t);
        }

        var candidates = new List<(InventoryElement R, int Ri, InventoryElement T, int Ti, double S)>();
        for (var i = 0; i < refs.Count; i++)
        {
            if (usedR.Contains(refs[i])) continue;
            for (var j = 0; j < tgts.Count; j++)
            {
                if (usedT.Contains(tgts[j])) continue;
                var s = Score(refs[i], tgts[j], profile);
                if (s >= minScore) candidates.Add((refs[i], i, tgts[j], j, s));
            }
        }
        foreach (var c in candidates.OrderByDescending(c => c.S).ThenBy(c => c.Ri).ThenBy(c => c.Ti))
        {
            if (usedR.Contains(c.R) || usedT.Contains(c.T)) continue;
            result.Pairs.Add(new MatchPair { Reference = c.R, Target = c.T, Score = Math.Round(c.S, 3), How = "auto" });
            usedR.Add(c.R); usedT.Add(c.T);
        }

        result.Pairs.Sort((a, b) => a.Reference.Index.CompareTo(b.Reference.Index));
        result.MissingOnTarget.AddRange(refs.Where(e => !usedR.Contains(e)));
        result.ExtraOnTarget.AddRange(tgts.Where(e => !usedT.Contains(e)));
        return result;
    }

    public static double Score(InventoryElement r, InventoryElement t, StackProfile profile)
    {
        if (!profile.RolesEquivalent(r.Role, t.Role)) return 0;
        var score = NameScore(r.DisplayName, t.DisplayName);

        if (r.RowIndex != null && r.ColIndex != null && t.RowIndex != null && t.ColIndex != null)
            score += r.RowIndex == t.RowIndex && r.ColIndex == t.ColIndex ? OrderWeight : 0;
        else
        {
            var d = Math.Abs(r.Order - t.Order);
            score += d == 0 ? OrderWeight : d == 1 ? OrderWeight / 2 : 0;
        }

        var dist = Math.Sqrt(Math.Pow(r.Box.CenterX - t.Box.CenterX, 2) + Math.Pow(r.Box.CenterY - t.Box.CenterY, 2));
        score += GeometryWeight * (1 - Math.Min(1, dist / ProximityPx));
        return score;
    }

    internal static double NameScore(string? a, string? b)
    {
        var na = NormalizeName(a);
        var nb = NormalizeName(b);
        if (na.Length == 0 && nb.Length == 0) return NameWeight / 2;
        if (na.Length == 0 || nb.Length == 0) return 0;
        if (na == nb) return NameWeight;
        var j = Jaccard(na, nb);
        return j >= 0.6 ? NameWeight * j : 0;
    }

    /// <summary>Lower case, whitespace collapsed, trailing ':' / '*' and surrounding punctuation removed.</summary>
    public static string NormalizeName(string? s)
    {
        var v = Regex.Replace((s ?? "").ToLowerInvariant(), @"\s+", " ").Trim();
        v = v.Trim(':', '*', '.', ' ', '\u00a0');
        return v;
    }

    /// <summary>
    /// A layout-table cell: an implicit-role "cell" (a td of a table without role=grid; explicit gridcells are always
    /// content) whose table has no columnheader/rowheader in the inventory AND that table is nested inside another
    /// table cell or contains a nested table. Such tables only position other things (WebForms/jqGrid pagers, toolbars);
    /// the controls inside them stay inventoried and compared. Generic (WCAG layout-table notion), no stack knowledge.
    /// A layout cell with its own text (<see cref="OwnText"/>, e.g. a pager caption or a form label in a layout table)
    /// is not dropped silently: AutoCssComparer compares that text.
    /// </summary>
    public static bool IsLayoutCell(InventoryElement e, IReadOnlyList<InventoryElement> inventory)
    {
        if (e.Role != "cell") return false;
        var table = TablePath(e.DomPath);
        if (table == null) return false;
        if (inventory.Any(h => h.Role is "columnheader" or "rowheader" && TablePath(h.DomPath) == table)) return false;
        var nestedInCell = Segments(table).SkipLast(1).Any(s => Tag(s) is "td" or "th");
        var containsTable = inventory.Any(o => TablePath(o.DomPath) is { } tp && tp.Length > table.Length && tp.StartsWith(table + " >", StringComparison.Ordinal));
        return nestedInCell || containsTable;
    }

    /// <summary>
    /// Visible text a cell carries itself: its name (else text) with the names, labels and texts of the inventory
    /// elements inside it removed (nested cells, controls, a select's option text). "" when nothing with a letter or
    /// digit is left, i.e. the cell only positions other compared elements. Whitespace collapsed.
    /// </summary>
    public static string OwnText(InventoryElement e, IReadOnlyList<InventoryElement> inventory)
    {
        var own = Regex.Replace(string.IsNullOrWhiteSpace(e.Name) ? e.Text : e.Name, @"\s+", " ").Trim();
        if (own.Length == 0 || e.DomPath.Length == 0) return own.Any(char.IsLetterOrDigit) ? own : "";
        var prefix = e.DomPath + " >";
        var inner = inventory.Where(o => !ReferenceEquals(o, e) && o.DomPath.StartsWith(prefix, StringComparison.Ordinal))
            .SelectMany(o => new[] { o.Name, o.Label, o.Text })
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(x => x.Length);
        foreach (var x in inner)
        {
            var i = own.IndexOf(x, StringComparison.Ordinal);
            if (i >= 0) own = own.Remove(i, x.Length);
        }
        own = Regex.Replace(own, @"\s+", " ").Trim();
        return own.Any(char.IsLetterOrDigit) ? own : "";
    }

    private static string[] Segments(string domPath) => domPath.Split(" > ", StringSplitOptions.TrimEntries);

    private static string Tag(string segment)
    {
        var i = segment.IndexOfAny(new[] { ':', '.', '#', '[' });
        return (i >= 0 ? segment[..i] : segment).ToLowerInvariant();
    }

    /// <summary>DOM path of the nearest enclosing table ("... > table:nth-of-type(1)"), null when not inside a table.</summary>
    internal static string? TablePath(string domPath)
    {
        var seg = Segments(domPath ?? "");
        for (var i = seg.Length - 1; i >= 0; i--)
            if (Tag(seg[i]) == "table") return string.Join(" > ", seg.Take(i + 1));
        return null;
    }

    internal static double Jaccard(string a, string b)
    {
        var ta = Regex.Split(a, @"[^\p{L}\p{N}]+").Where(x => x.Length > 0).ToHashSet();
        var tb = Regex.Split(b, @"[^\p{L}\p{N}]+").Where(x => x.Length > 0).ToHashSet();
        if (ta.Count == 0 || tb.Count == 0) return 0;
        return (double)ta.Intersect(tb).Count() / ta.Union(tb).Count();
    }
}

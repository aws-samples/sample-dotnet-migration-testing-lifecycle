using System.Security.Cryptography;
using System.Text;
using MigrationTests.Playwright;

namespace MigrationTests.Reporting;

/// <summary>One page-level CSS finding: every raw row with the same grouping key (runs/css_groups.json).</summary>
public sealed class CssGroup
{
    public const string Theme = "theme";
    public const string Individual = "individual";

    /// <summary>Display id G1..Gn (actionable first, then severity, element count, key).</summary>
    public string Id { get; set; } = "";
    /// <summary>Stable 10-hex key of the grouping key (fixer id css:group:&lt;key&gt;; survives re-runs).</summary>
    public string Key { get; set; } = "";
    /// <summary>theme (role class + property + values: one CSS rule / theme token) | individual (element-specific).</summary>
    public string Kind { get; set; } = Theme;
    public string Layer { get; set; } = "";
    /// <summary>Role class (first role of the profile equivalence group), the role, or "el:&lt;element&gt;" for role-less rows.</summary>
    public string RoleClass { get; set; } = "";
    public string Property { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Target { get; set; } = "";
    /// <summary>Max severity of the members.</summary>
    public string Severity { get; set; } = "LOW";
    public List<string> Elements { get; set; } = new();
    public List<string> Examples { get; set; } = new();
    public int ElementCount { get; set; }
    public List<string> States { get; set; } = new();
    /// <summary>Modules the group was seen in, plan order.</summary>
    public List<string> Modules { get; set; } = new();
    /// <summary>First module in plan order: the group is counted (and fixed) there.</summary>
    public string Owner { get; set; } = "";
    public int RowCount { get; set; }
    public bool Actionable { get; set; }
    public bool Accepted { get; set; }
    public string? AcceptedNote { get; set; }
    /// <summary>"css-map" or "profile &lt;name&gt;" when accepted.</summary>
    public string? AcceptedBy { get; set; }
    public string Action { get; set; } = "";
    /// <summary>Distinct severity rules of the members (audit).</summary>
    public List<string> SeverityRules { get; set; } = new();
    /// <summary>One plain-English sentence describing the difference (IssueText; the fixer's task title).</summary>
    public string Issue { get; set; } = "";
    /// <summary>Interactive states folded into this base-state group because their values are the same ("hover", "focus", "disabled").</summary>
    public List<string> AlsoIn { get; set; } = new();
    /// <summary>Every member is a responsive row where only the Reference overflows/clips (old-page problem, not counted).</summary>
    public bool ReferenceWorse { get; set; }
    /// <summary>The group shows in every planned CSS state of the page ("all N states" in Seen in).</summary>
    public bool InAllStates { get; set; }
}

/// <summary>Every raw CSS row with its group (runs/css_rows_all.json / .md).</summary>
public sealed class CssAuditRow
{
    public string Module { get; set; } = "";
    public string Element { get; set; } = "";
    public string State { get; set; } = "";
    public string Property { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Target { get; set; } = "";
    public string Severity { get; set; } = "";
    public string? SeverityRule { get; set; }
    public string Layer { get; set; } = "";
    public bool Accepted { get; set; }
    public bool Actionable { get; set; }
    public string GroupId { get; set; } = "";
    public string GroupKey { get; set; } = "";
}

public sealed class CssGroupResult
{
    public List<CssGroup> Groups { get; set; } = new();
    public List<CssAuditRow> Rows { get; set; } = new();
}

/// <summary>runs/css_groups.json.</summary>
public sealed class CssGroupsFile
{
    public string Page { get; set; } = "";
    public string Profile { get; set; } = "";
    public List<string> Modules { get; set; } = new();
    public int RawRows { get; set; }
    public int ActionableRows { get; set; }
    public int ActionableGroups { get; set; }
    public List<CssGroup> Groups { get; set; } = new();
}

/// <summary>
/// Groups the raw CSS rows of every module of a page (pure, deterministic). Theme layers (mapped non-geometry, style,
/// state) group by (role class, property, normalized Reference, normalized Target, accepted); element-specific rows
/// (match, layout, responsive, geometry, "(...)" properties) group by (layer, element, property, values, accepted) so
/// only identical repeats across states/modules collapse. Every raw row maps to exactly one group; accepted rows never
/// merge with open ones; group severity = max member severity.
/// </summary>
public static class CssGrouper
{
    public static bool IsTheme(CssDiffEntry d) =>
        d.Layer is CssLayers.Style or CssLayers.State or CssLayers.Mapped or ""
        && !d.Property.StartsWith("(", StringComparison.Ordinal)
        && !d.Property.StartsWith(ModuleReportGenerator.LayoutPrefix, StringComparison.Ordinal);

    /// <summary>First role (ordinal, lower-case) of the profile equivalence group containing role; the role itself; else el:&lt;element&gt;.</summary>
    public static string RoleClass(CssDiffEntry d, StackProfile? profile)
    {
        var role = d.Role?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(role)) return "el:" + d.Element;
        var eq = profile?.RoleEquivalences.FirstOrDefault(g => g.Contains(role, StringComparer.OrdinalIgnoreCase));
        return eq == null ? role : eq.Select(r => r.Trim().ToLowerInvariant()).OrderBy(r => r, StringComparer.Ordinal).First();
    }

    internal static string GroupingKey(CssDiffEntry d, StackProfile? profile) => GroupingKey(d, profile, d.Property);

    /// <summary>Grouping key with the effective property (merged border sides, folded interactive states).</summary>
    internal static string GroupingKey(CssDiffEntry d, StackProfile? profile, string property)
    {
        var layer = string.IsNullOrEmpty(d.Layer) ? CssLayers.Mapped : d.Layer;
        return IsTheme(d)
            ? string.Join("\u001f", "theme", RoleClass(d, profile), property,
                StyleNormalizer.Canonical(d.Property, d.Reference), StyleNormalizer.Canonical(d.Property, d.Target), Acc(d))
            : string.Join("\u001f", "individual", layer, d.Element, property, d.Reference, d.Target, Acc(d));
    }

    private static readonly string[] BorderSides = { "top", "right", "bottom", "left" };
    private static readonly string[] StatePrefixes = { "hover", "focus", "disabled" };

    private static (string Prefix, string Base) SplitState(string property)
    {
        var dot = property.IndexOf('.');
        if (dot > 0 && StatePrefixes.Contains(property[..dot])) return (property[..dot], property[(dot + 1)..]);
        return ("", property);
    }

    /// <summary>
    /// Effective grouping property per theme row. (1) Border merge: border-top/right/bottom/left rows of the same element,
    /// state and module with identical normalized values become one "border" (all four sides) or "border (top, bottom)"
    /// property; sides with other values stay separate. (2) State fold: a hover./focus./disabled. row whose normalized
    /// values equal the base-state row of the same element, state, module and property takes the base row's property, so it
    /// joins the base group (the group lists the state in AlsoIn). Raw rows are never changed.
    /// </summary>
    internal static (Dictionary<CssDiffEntry, string> Property, Dictionary<CssDiffEntry, string> Folded) EffectiveProperties(
        IReadOnlyList<(string Module, CssDiffEntry Row)> rows)
    {
        var eff = new Dictionary<CssDiffEntry, string>(ReferenceEqualityComparer.Instance);
        var folded = new Dictionary<CssDiffEntry, string>(ReferenceEqualityComparer.Instance);
        foreach (var (_, r) in rows) eff[r] = r.Property;

        string Canon(CssDiffEntry d) => StyleNormalizer.Canonical(d.Property, d.Reference) + "\u001f" + StyleNormalizer.Canonical(d.Property, d.Target);

        var sideRows = rows.Where(x => IsTheme(x.Row))
            .Select(x => (x.Module, x.Row, Split: SplitState(x.Row.Property)))
            .Select(x => (x.Module, x.Row, x.Split.Prefix, Side: x.Split.Base.StartsWith("border-", StringComparison.Ordinal) ? x.Split.Base["border-".Length..] : ""))
            .Where(x => BorderSides.Contains(x.Side))
            .GroupBy(x => string.Join("\u001f", x.Module, x.Row.Element, x.Row.State, x.Row.Layer, x.Prefix, Canon(x.Row), Acc(x.Row)), StringComparer.Ordinal);
        foreach (var g in sideRows)
        {
            var sides = BorderSides.Where(s => g.Any(x => x.Side == s)).ToList();
            if (sides.Count < 2) continue;
            var prefix = g.First().Prefix;
            var merged = (prefix.Length > 0 ? prefix + "." : "") + (sides.Count == 4 ? "border" : $"border ({string.Join(", ", sides)})");
            foreach (var x in g) eff[x.Row] = merged;
        }

        var bases = new Dictionary<string, CssDiffEntry>(StringComparer.Ordinal);
        foreach (var (m, r) in rows)
            if (IsTheme(r) && SplitState(r.Property).Prefix.Length == 0)
                bases.TryAdd(string.Join("\u001f", m, r.Element, r.State, eff[r], Canon(r), Acc(r)), r);
        foreach (var (m, r) in rows)
        {
            if (!IsTheme(r)) continue;
            var (prefix, _) = SplitState(eff[r]);
            if (prefix.Length == 0) continue;
            var baseProp = eff[r][(prefix.Length + 1)..];
            if (bases.TryGetValue(string.Join("\u001f", m, r.Element, r.State, baseProp, Canon(r), Acc(r)), out var b))
            {
                eff[r] = eff[b];
                folded[r] = prefix;
            }
        }
        return (eff, folded);
    }

    // Accepted rows never merge with open ones, nor with rows accepted for another reason.
    private static string Acc(CssDiffEntry d) => d.Accepted ? $"accepted:{d.AcceptedBy}:{d.AcceptedReason}" : "open";

    internal static string StableKey(string groupingKey)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(groupingKey));
        return Convert.ToHexString(hash)[..10].ToLowerInvariant();
    }

    internal static string Plural(string roleClass)
    {
        if (roleClass.StartsWith("el:", StringComparison.Ordinal)) return roleClass[3..];
        var n = roleClass.Length == 0 ? "Elements" : char.ToUpperInvariant(roleClass[0]) + roleClass[1..];
        if (n.EndsWith('s')) return n;
        // combobox, checkbox, listbox, textbox -> "-boxes"
        return n.EndsWith('x') ? n + "es" : n + "s";
    }

    public static CssGroupResult Build(IReadOnlyList<(string Module, CssDiffEntry Row)> rows, IReadOnlyList<string> moduleOrder,
        StackProfile? profile, CssGroupsSettings? settings = null, IssueContext? issue = null)
    {
        settings ??= new CssGroupsSettings();
        issue ??= new IssueContext();
        var (effective, folded) = EffectiveProperties(rows);
        int ModRank(string m)
        {
            for (var i = 0; i < moduleOrder.Count; i++)
                if (string.Equals(moduleOrder[i], m, StringComparison.OrdinalIgnoreCase)) return i;
            return int.MaxValue;
        }

        var buckets = new Dictionary<string, List<(string Module, CssDiffEntry Row)>>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var k = GroupingKey(r.Row, profile, effective[r.Row]);
            if (!buckets.TryGetValue(k, out var list)) buckets[k] = list = new();
            list.Add(r);
        }

        var groups = new List<(CssGroup G, List<(string Module, CssDiffEntry Row)> Members)>();
        foreach (var (gk, members) in buckets)
        {
            // Representative: the first base-state member (folded hover/focus/disabled rows only join it).
            var first = members.Select(m => m.Row).FirstOrDefault(r => !folded.ContainsKey(r)) ?? members[0].Row;
            var theme = IsTheme(first);
            var modules = members.Select(m => m.Module).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ModRank).ThenBy(m => m, StringComparer.Ordinal).ToList();
            var elements = members.Select(m => m.Row.Element).Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal).ToList();
            var severity = members.Select(m => m.Row.Severity).OrderBy(ModuleReportGenerator.SevRank).First();
            var g = new CssGroup
            {
                Key = StableKey(gk),
                Kind = theme ? CssGroup.Theme : CssGroup.Individual,
                Layer = string.IsNullOrEmpty(first.Layer) ? CssLayers.Mapped : first.Layer,
                RoleClass = RoleClass(first, profile),
                Property = effective[first],
                Reference = first.Reference,
                Target = first.Target,
                Severity = severity,
                Elements = elements,
                Examples = elements.Take(Math.Max(1, settings.ExampleElements)).ToList(),
                ElementCount = elements.Count,
                States = members.Select(m => m.Row.State).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
                Modules = modules,
                Owner = modules[0],
                RowCount = members.Count,
                Actionable = members.Any(m => m.Row.Actionable),
                Accepted = first.Accepted,
                AcceptedNote = first.Accepted ? ModuleReportGenerator.AcceptedNote(first) : null,
                AcceptedBy = first.Accepted ? first.AcceptedBy : null,
                SeverityRules = members.Select(m => m.Row.SeverityRule).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!)
                    .Distinct(StringComparer.Ordinal).ToList(),
                AlsoIn = StatePrefixes.Where(p => members.Any(m => folded.TryGetValue(m.Row, out var f) && f == p)).ToList(),
                ReferenceWorse = members.All(m => ModuleReportGenerator.IsReferenceWorse(m.Row))
            };
            g.InAllStates = issue.AllStates.Count > 0 && issue.AllStates.All(s => g.States.Contains(s, StringComparer.Ordinal));
            var memberRows = members.Select(m => m.Row).ToList();
            g.Action = GroupAction(g, memberRows, first);
            g.Issue = IssueText.For(g, memberRows, issue);
            groups.Add((g, members));
        }

        var ordered = groups
            .OrderBy(x => x.G.Actionable ? 0 : 1)
            .ThenBy(x => ModuleReportGenerator.SevRank(x.G.Severity))
            .ThenByDescending(x => x.G.ElementCount)
            .ThenBy(x => x.G.Key, StringComparer.Ordinal)
            .ToList();
        var result = new CssGroupResult();
        for (var i = 0; i < ordered.Count; i++)
        {
            var (g, members) = ordered[i];
            g.Id = $"G{i + 1}";
            foreach (var (module, row) in members)
            {
                row.GroupId = g.Id;
                row.GroupKey = g.Key;
            }
            result.Groups.Add(g);
        }
        // Audit rows in input order (module order as given, then row order).
        foreach (var (module, row) in rows)
            result.Rows.Add(new CssAuditRow
            {
                Module = module, Element = row.Element, State = row.State, Property = row.Property,
                Reference = row.Reference, Target = row.Target, Severity = row.Severity, SeverityRule = row.SeverityRule,
                Layer = string.IsNullOrEmpty(row.Layer) ? CssLayers.Mapped : row.Layer, Accepted = row.Accepted,
                Actionable = row.Actionable, GroupId = row.GroupId ?? "", GroupKey = row.GroupKey ?? ""
            });
        return result;
    }

    /// <summary>
    /// Theme group: "Buttons: border-radius 4px -> 3px, 46 elements: one CSS rule / theme token in the Target (...)".
    /// A member "Reference larger" action (never pad the Target) is kept as-is; individual groups use the first member's action.
    /// </summary>
    internal static string GroupAction(CssGroup g, List<CssDiffEntry> members, CssDiffEntry? representative = null)
    {
        string Of(CssDiffEntry d) => string.IsNullOrEmpty(d.Action) ? ModuleReportGenerator.ActionFor(d) : d.Action;
        var refLarger = members.Select(Of).FirstOrDefault(a => a.StartsWith("Reference larger", StringComparison.Ordinal));
        if (refLarger != null) return refLarger;
        var rep = representative ?? members[0];
        var first = Of(rep);
        // Merged border sides: one instruction for every side of the group.
        if (g.Property != rep.Property && IssueText.BorderSide(SplitState(g.Property).Base) != null)
        {
            var (prefix, prop) = SplitState(g.Property);
            var css = prop == "border" ? "border" : string.Join(", ",
                System.Text.RegularExpressions.Regex.Match(prop, @"\((?<s>[^)]*)\)").Groups["s"].Value.Split(',').Select(s => "border-" + s.Trim()));
            first = $"Align to reference: set {css}: {g.Reference}{(prefix.Length > 0 ? " on :" + prefix : "")}";
        }
        if (g.AlsoIn.Count > 0) first += $" (same values on {string.Join("/", g.AlsoIn.Select(s => ":" + s))})";
        if (g.Kind != CssGroup.Theme) return first;
        var n = g.ElementCount == 1 ? "1 element" : $"{g.ElementCount} elements";
        return $"{Plural(g.RoleClass)}: {g.Property} {g.Target} -> {g.Reference}, {n}: change one CSS rule / theme token in the Target ({first})";
    }
}

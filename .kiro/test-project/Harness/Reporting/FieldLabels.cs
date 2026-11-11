using System.Globalization;
using System.Text.RegularExpressions;

namespace MigrationTests.Reporting;

/// <summary>
/// Plain-English step descriptions (P7). Field names come from plan.json "fieldLabels" (pattern → label with
/// {n} / {name} placeholders); unknown fields are humanized ("grid.rowCount" → "Grid row count").
/// Deterministic: the same results.json and plan.json always give the same text.
/// </summary>
public sealed class FieldLabels
{
    private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private readonly List<(Regex Re, string Template, int Literal)> _patterns = new();

    public FieldLabels(IReadOnlyDictionary<string, string>? map = null)
    {
        foreach (var (pattern, label) in map ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(label)) continue;
            if (!pattern.Contains("{n}") && !pattern.Contains("{name}")) { _exact[pattern] = label; continue; }
            var rx = Regex.Escape(pattern).Replace(@"\{n}", @"(?<n>\d+)").Replace(@"\{name}", @"(?<name>[^.\[\]]+)");
            _patterns.Add((new Regex("^" + rx + "$", RegexOptions.CultureInvariant), label, pattern.Replace("{n}", "").Replace("{name}", "").Length));
        }
        // Most specific pattern first (most literal characters), then declaration order.
        _patterns = _patterns.Select((p, i) => (p, i)).OrderByDescending(x => x.p.Literal).ThenBy(x => x.i).Select(x => x.p).ToList();
    }

    /// <summary>Label for a field: exact entry, else the most specific pattern, else <see cref="Humanize"/>.</summary>
    public string Label(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return "Value";
        if (TryMapped(field, out var label)) return label;
        // Page health fields (page.failedRequest, ...): the fixed plain-English kind.
        if (field.StartsWith("page.", StringComparison.Ordinal) && PropertyWords.TryGetValue(field, out var page)) return Capitalize(page);
        return Humanize(field);
    }

    private bool TryMapped(string field, out string label)
    {
        if (_exact.TryGetValue(field, out label!)) return true;
        foreach (var (re, template, _) in _patterns)
        {
            var m = re.Match(field);
            if (!m.Success) continue;
            label = template.Replace("{n}", m.Groups["n"].Value).Replace("{name}", m.Groups["name"].Value);
            return true;
        }
        label = "";
        return false;
    }

    private static readonly Regex Token = new(@"\[([^\]]*)\]|[^.\[\]]+", RegexOptions.CultureInvariant);
    private static readonly Regex Camel = new(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.CultureInvariant);

    /// <summary>"row[3].name.text" → "Row 3 name"; "grid.rowCount" → "Grid row count"; "sort.indicator[Name]" → "Sort indicator Name".</summary>
    public static string Humanize(string field)
    {
        var f = field.Trim();
        if (f.EndsWith(".text", StringComparison.Ordinal) && f.Length > 5) f = f[..^5];
        var words = new List<string>();
        foreach (Match m in Token.Matches(f))
        {
            if (m.Groups[1].Success) { if (m.Groups[1].Value.Length > 0) words.Add(m.Groups[1].Value); continue; }
            words.AddRange(Camel.Split(m.Value).Where(w => w.Length > 0).Select(w => w.ToLowerInvariant()));
        }
        return words.Count == 0 ? field : Capitalize(string.Join(" ", words));
    }

    // ───────────────────────────── CSS diff properties ─────────────────────────────

    private static readonly Dictionary<string, string> PropertyWords = new(StringComparer.Ordinal)
    {
        ["(missing on Target)"] = "missing on Target",
        ["(extra on Target)"] = "extra on Target",
        ["(layout text not found on Target)"] = "layout-table text not found on Target",
        ["(layout text only on Target)"] = "layout-table text only on Target",
        ["(element)"] = "element missing on Target",
        ["(unmeasured)"] = "geometry not measured",
        ["(not captured on Target)"] = "state not captured on Target",
        ["(css-map.json missing)"] = "no css-map.json",
        ["text.name"] = "accessible name text",
        ["text.label"] = "label text",
        ["text.placeholder"] = "placeholder text",
        ["layout.order"] = "reading order",
        ["layout.align"] = "alignment with the previous element",
        ["layout.labelPosition"] = "label position",
        ["layout.spacing"] = "spacing to the previous element",
        ["layout.overflow"] = "text overflow",
        ["layout.lines"] = "line wrapping",
        ["layout.overlap"] = "overlap with another element",
        ["layout.hidden"] = "visibility (clipped)",
        ["layout.height"] = "grid height",
        ["layout.rowHeight"] = "row height",
        ["layout.headerHeight"] = "header height",
        ["layout.gapToPager"] = "gap between last row and pager",
        ["layout.gapToGrid"] = "gap between filter and grid",
        ["page.leakedSyntax"] = "leaked server/template syntax",
        ["page.consoleError"] = "browser console error",
        ["page.pageError"] = "unhandled page error",
        ["page.brokenImage"] = "broken image",
        ["page.failedRequest"] = "failed network request",
        ["page.emptySection"] = "empty section",
        ["page.a11y"] = "accessibility gap"
    };

    private static readonly Dictionary<string, string> ResponsiveWords = new(StringComparer.Ordinal)
    {
        ["overflow"] = "overflows", ["lines"] = "wraps differently", ["hidden"] = "hidden or clipped"
    };

    /// <summary>
    /// Plain-English CSS diff property: "layout.order" → "reading order", "hover.background-color" → "background colour on hover",
    /// "responsive.768.overflow" → "overflows at 768px width", "(missing on Target)" → "missing on Target".
    /// Plain CSS property names stay as they are ("font-size"): developers read them directly.
    /// </summary>
    public static string PropertyText(string? property)
    {
        var p = (property ?? "").Trim();
        if (p.Length == 0) return "";
        if (PropertyWords.TryGetValue(p, out var w)) return w;
        if (p.StartsWith("(unmeasured: needs role ", StringComparison.Ordinal)) return "geometry not measured (" + p[("(unmeasured: ".Length)..^1] + ")";
        var parts = p.Split('.');
        if (parts.Length == 3 && parts[0] == "responsive")
            return $"{(ResponsiveWords.TryGetValue(parts[2], out var rw) ? rw : PropertyText(parts[2]))} at {parts[1]}px width";
        if (parts.Length == 2 && parts[0] is "hover" or "focus" or "disabled")
            return $"{PropertyText(parts[1])} {(parts[0] == "disabled" ? "when disabled" : "on " + parts[0])}";
        if (parts.Length >= 2 && parts[0] is "layout" or "text" or "page") return Humanize(p).ToLowerInvariant();
        return p;
    }

    /// <summary>Audit form: "reading order (layout.order)"; unchanged when the text is the property itself.</summary>
    public static string PropertyAudit(string? property)
    {
        var t = PropertyText(property);
        return string.Equals(t, property, StringComparison.Ordinal) ? property ?? "" : $"{t} ({property})";
    }

    // ───────────────────────────── booleans ─────────────────────────────

    private static readonly string[] BoolSuffixes = { ".enabled", ".disabled", ".visible" };

    /// <summary>Subject of a boolean field: the mapped label of the full field, else the label of the field without its suffix.</summary>
    private string BoolSubject(string field, string suffix)
    {
        if (TryMapped(field, out var label)) return label;
        return Label(field[..^suffix.Length]);
    }

    private static string? BoolSuffix(string field) => BoolSuffixes.FirstOrDefault(s => field.EndsWith(s, StringComparison.Ordinal));

    private static bool? ParseBool(string? v) =>
        string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ? true
        : string.Equals(v?.Trim(), "false", StringComparison.OrdinalIgnoreCase) ? false : null;

    /// <summary>"enabled"/"disabled"/"visible"/"hidden" for a boolean field value, else null.</summary>
    private static string? BoolWord(string suffix, string? value)
    {
        var b = ParseBool(value);
        if (b == null) return null;
        return suffix switch
        {
            ".enabled" => b.Value ? "enabled" : "disabled",
            ".disabled" => b.Value ? "disabled" : "enabled",
            _ => b.Value ? "visible" : "hidden"
        };
    }

    // ───────────────────────────── sentences ─────────────────────────────

    private sealed record Clause(string Subject, string Predicate, bool Mergeable, string Field);

    /// <summary>Quotes text values; numbers and true/false stay bare; empty → "(empty)"; long values are shortened.</summary>
    public static string Q(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "(empty)";
        var t = v.Replace("\r", " ").Replace("\n", " ");
        if (t.Length > 60) t = t[..60] + "...";
        if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) || ParseBool(t) != null) return t;
        return "'" + t + "'";
    }

    /// <summary>Sort-state tokens of sort fields in plain English.</summary>
    private static readonly Dictionary<string, string> SortWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["asc"] = "ascending", ["ascending"] = "ascending", ["desc"] = "descending", ["descending"] = "descending", ["none"] = "not sorted"
    };

    /// <summary>A field holding a sort state ("sort.indicator[Name]", "grid.column[Code].sortDirection").</summary>
    internal static bool IsSortField(string? field) =>
        !string.IsNullOrEmpty(field) && field.Contains("sort", StringComparison.OrdinalIgnoreCase) && !IsSequenceField(field);

    /// <summary>Q with plain words for sort states: "Sort indicator on Name is ascending" instead of "'asc'".</summary>
    private static string QV(string? field, string? v) =>
        IsSortField(field) && v != null && SortWords.TryGetValue(v.Trim(), out var w) ? w : Q(v);

    /// <summary>
    /// A recorded value in plain English for report cells: sort states ("asc" -> "ascending"), booleans of
    /// .enabled/.disabled/.visible fields ("true" -> "enabled"), the classifier placeholders ("(not asserted)" ->
    /// "not checked on the new page"); any other value unchanged. The raw value stays in the audit files.
    /// </summary>
    public static string ValueText(string? field, string? value)
    {
        var v = value ?? "";
        switch (v.Trim())
        {
            case "(not asserted)": return "not checked on the new page (no matching check in the Target test)";
            case "(not captured)": return "not captured on the new page";
        }
        if (IsSortField(field) && SortWords.TryGetValue(v.Trim(), out var s)) return s;
        if (field != null && BoolSuffix(field) is { } suffix && BoolWord(suffix, v) is { } b) return b;
        return v;
    }

    // ───────────────────────────── list values (chat) ─────────────────────────────

    private static readonly Regex ColumnName = new(@"column\[(?<name>[^\]]+)\]", RegexOptions.CultureInvariant);

    /// <summary>Lists shorter than this stay verbatim in chat ('1,2,3' is clearer than a summary).</summary>
    private const int LongListChars = 40;

    /// <summary>An order/sequence field (e.g. grid.column[Name].sequence): summarized by sort direction.</summary>
    internal static bool IsSequenceField(string? field) =>
        !string.IsNullOrEmpty(field) &&
        (field.Contains("sequence", StringComparison.OrdinalIgnoreCase) || field.EndsWith(".order", StringComparison.OrdinalIgnoreCase));

    /// <summary>A list value (',' or '|' separated) worth summarizing: any list of a sequence field, else a long list of 3+ items.</summary>
    internal static bool IsListField(string? field, string? value)
    {
        if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(value)) return false;
        var items = ListItems(value).Count;
        return IsSequenceField(field) ? items >= 2 : items >= 3 && value.Length > LongListChars;
    }

    private static List<string> ListItems(string v) =>
        v.Split(v.Contains('|') ? '|' : ',').Select(s => s.Trim()).Select(s => s.EndsWith("...", StringComparison.Ordinal) ? s[..^3].TrimEnd() : s)
            .Where(s => s.Length > 0).ToList();

    /// <summary>"6 items (first: X, last: Y)" for a list that is not an order/sequence.</summary>
    internal static string ListText(string? v)
    {
        var items = ListItems(v ?? "");
        return items.Count == 0 ? "(empty)" : $"{items.Count} item{(items.Count == 1 ? "" : "s")} (first: {items[0]}, last: {items[^1]})";
    }

    /// <summary>"A to Z" / "Z to A" (text) or "lowest to highest" / "highest to lowest" (numbers); null when unsorted or all equal.</summary>
    internal static string? SortDirection(IReadOnlyList<string> items)
    {
        if (items.Count < 2) return null;
        var numeric = items.All(i => double.TryParse(i, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        int Cmp(string a, string b) => numeric
            ? double.Parse(a, CultureInfo.InvariantCulture).CompareTo(double.Parse(b, CultureInfo.InvariantCulture))
            : string.Compare(a, b, StringComparison.InvariantCultureIgnoreCase);
        var c = items.Zip(items.Skip(1), Cmp).ToList();
        var asc = c.All(x => x <= 0);
        var desc = c.All(x => x >= 0);
        if (asc == desc) return null; // unsorted, or every item equal
        return asc ? (numeric ? "lowest to highest" : "A to Z") : (numeric ? "highest to lowest" : "Z to A");
    }

    /// <summary>"A to Z (first: X)" for a sorted list, else "(first: X, last: Y)" (the caller supplies the verb).</summary>
    internal static (string? Direction, string Ends) SequenceParts(string? v)
    {
        var items = ListItems(v ?? "");
        if (items.Count == 0) return (null, "(empty)");
        var dir = SortDirection(items);
        return dir != null ? (dir, $"(first: {items[0]})") : (null, $"(first: {items[0]}, last: {items[^1]})");
    }

    /// <summary>Plain-English list value: "A to Z (first: X)" or "in no sort order (first: X, last: Y)".</summary>
    internal static string SequenceText(string? v)
    {
        var (dir, ends) = SequenceParts(v);
        return dir != null ? $"{dir} {ends}" : $"in no sort order {ends}";
    }

    /// <summary>Subject of a list field: "Name column order" for column[Name], else the field label.</summary>
    private string ListSubject(string field) =>
        ColumnName.Match(field) is { Success: true } m ? $"{m.Groups["name"].Value} column order" : Label(field);

    /// <summary>Chat clause for an order/sequence field: summarized, never the full list.</summary>
    private Clause ListPassClause(RunAssertion a)
    {
        var subject = ListSubject(a.Field);
        if (!IsSequenceField(a.Field))
            return a.Type switch
            {
                "CHANGES" => new Clause(subject, $"changed: now {ListText(a.Actual)}", false, a.Field),
                "STAYS SAME" => new Clause(subject, $"stayed the same: {ListText(a.Actual)}", false, a.Field),
                _ => new Clause(subject, $"shows {ListText(a.Actual)}", false, a.Field)
            };
        var (dir, ends) = SequenceParts(a.Actual);
        return a.Type switch
        {
            "CHANGES" => new Clause(subject, dir != null ? $"changed: now {dir} {ends}" : $"changed {ends}", false, a.Field),
            "STAYS SAME" => new Clause(subject, dir != null ? $"stayed {dir} {ends}" : $"stayed the same {ends}", false, a.Field),
            _ => new Clause(subject, $"is {SequenceText(a.Actual)}", false, a.Field)
        };
    }

    private Clause ListFailClause(RunAssertion a, string src)
    {
        var subject = ListSubject(a.Field);
        Func<string?, string> text = IsSequenceField(a.Field) ? SequenceText : ListText;
        return a.Type switch
        {
            "CHANGES" => new Clause(subject, $"did not change (still {text(a.Expected)})", false, a.Field),
            "STAYS SAME" => new Clause(subject, $"changed unexpectedly from {text(a.Expected)} to {text(a.Actual)}", false, a.Field),
            _ => new Clause(subject, $"is {text(a.Actual)}, expected {text(a.Expected)}{src}", false, a.Field)
        };
    }

    private static bool IsList(RunAssertion a) =>
        a.Type is "CHANGES" or "STAYS SAME" or "EQUALS" && (IsListField(a.Field, a.Actual) || IsListField(a.Field, a.Expected));

    private Clause? PassClause(RunAssertion a, IReadOnlyCollection<string> coveredFields, bool summarizeLists = false)
    {
        if (summarizeLists && IsList(a) && !(a.Type == "EQUALS" && coveredFields.Contains(a.Field))) return ListPassClause(a);
        var suffix = BoolSuffix(a.Field);
        switch (a.Type)
        {
            case "CHANGES":
                if (suffix != null && BoolWord(suffix, a.Actual) is { } cw)
                    return new Clause(BoolSubject(a.Field, suffix), suffix == ".visible" ? $"is now {cw}" : $"became {cw}", true, a.Field);
                if (string.IsNullOrEmpty(a.Expected)) return new Clause(Label(a.Field), $"shown: {QV(a.Field, a.Actual)}", false, a.Field);
                if (string.IsNullOrEmpty(a.Actual)) return new Clause(Label(a.Field), "cleared", true, a.Field);
                return new Clause(Label(a.Field), $"changed from {QV(a.Field, a.Expected)} to {QV(a.Field, a.Actual)}", false, a.Field);
            case "STAYS SAME":
                if (suffix != null && BoolWord(suffix, a.Actual) is { } sw)
                    return new Clause(BoolSubject(a.Field, suffix), $"stayed {sw}", true, a.Field);
                return new Clause(Label(a.Field), $"stayed {QV(a.Field, a.Actual)}", false, a.Field);
            case "EQUALS":
                if (coveredFields.Contains(a.Field)) return null;
                if (suffix != null && BoolWord(suffix, a.Actual) is { } ew)
                    return new Clause(BoolSubject(a.Field, suffix), $"is {ew}", true, a.Field);
                return new Clause(Label(a.Field), $"is {QV(a.Field, a.Actual)}", false, a.Field);
            case "CONTAINS":
                return new Clause(Label(a.Field), $"contains {QV(a.Field, a.Expected)}", false, a.Field);
            default: // BOOL: the test's own wording
                return new Clause("", string.IsNullOrWhiteSpace(a.Message) ? $"{Label(a.Field)} check passed" : a.Message.Trim().TrimEnd('.'), false, a.Field);
        }
    }

    private Clause FailClause(RunAssertion a, bool isTarget, bool summarizeLists = false)
    {
        var src = isTarget ? " (Reference)" : "";
        if (summarizeLists && IsList(a)) return ListFailClause(a, src);
        switch (a.Type)
        {
            case "EQUALS":
                return new Clause(Label(a.Field), $"is {QV(a.Field, a.Actual)}, expected {QV(a.Field, a.Expected)}{src}", false, a.Field);
            case "CHANGES":
                return new Clause(Label(a.Field), $"did not change (still {QV(a.Field, a.Expected)})", false, a.Field);
            case "STAYS SAME":
                return new Clause(Label(a.Field), $"changed unexpectedly from {QV(a.Field, a.Expected)} to {QV(a.Field, a.Actual)}", false, a.Field);
            case "CONTAINS":
                return new Clause(Label(a.Field), $"is {QV(a.Field, a.Actual)}, expected to contain {QV(a.Field, a.Expected)}{src}", false, a.Field);
            default:
                return new Clause("", string.IsNullOrWhiteSpace(a.Message) ? $"{Label(a.Field)} check failed" : a.Message.Trim().TrimEnd('.'), false, a.Field);
        }
    }

    /// <summary>Joins clauses into sentences; mergeable clauses with the same predicate share one sentence.</summary>
    private static List<string> Join(IEnumerable<Clause> clauses, bool withFieldNames)
    {
        var groups = new List<(List<string> Subjects, string Predicate, List<string> Fields)>();
        var byPredicate = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in clauses)
        {
            if (c.Mergeable && byPredicate.TryGetValue(c.Predicate, out var gi))
            {
                if (!groups[gi].Subjects.Contains(c.Subject)) groups[gi].Subjects.Add(c.Subject);
                groups[gi].Fields.Add(c.Field);
                continue;
            }
            groups.Add((new List<string> { c.Subject }, c.Predicate, new List<string> { c.Field }));
            if (c.Mergeable) byPredicate[c.Predicate] = groups.Count - 1;
        }
        return groups.Select(g =>
        {
            var subject = JoinWords(g.Subjects.Where(s => s.Length > 0).ToList());
            var text = subject.Length == 0 ? g.Predicate : $"{subject} {g.Predicate}";
            var fields = g.Fields.Where(f => f.Length > 0).Distinct().ToList();
            if (withFieldNames && fields.Count > 0) text += $" ({(fields.Count == 1 ? "field" : "fields")}: {string.Join(", ", fields)})";
            return EndSentence(Capitalize(text));
        }).ToList();
    }

    /// <summary>Adds a period unless the text already ends a sentence (also inside a closing quote: "... 'Saved.'").</summary>
    private static string EndSentence(string s)
    {
        var t = s.TrimEnd();
        var core = t.TrimEnd('\'', '"');
        return core.EndsWith('.') || core.EndsWith('!') || core.EndsWith('?') ? t : t + ".";
    }

    private static string JoinWords(IReadOnlyList<string> w) =>
        w.Count switch { 0 => "", 1 => w[0], _ => string.Join(", ", w.Take(w.Count - 1)) + " and " + w[^1] };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>One sentence per check of the step (failed checks first). Booleans with the same verb are merged.</summary>
    public List<string> Sentences(RunStep step, bool isTarget, bool withFieldNames = false)
    {
        var failed = step.Assertions.Where(a => !a.Passed).Select(a => FailClause(a, isTarget)).ToList();
        var covered = step.Assertions.Where(a => a.Passed && a.Type is "CHANGES" or "STAYS SAME").Select(a => a.Field).ToHashSet(StringComparer.Ordinal);
        var passed = step.Assertions.Where(a => a.Passed).Select(a => PassClause(a, covered)).Where(c => c != null).Cast<Clause>().ToList();
        return Join(failed, withFieldNames).Concat(Join(passed, withFieldNames)).ToList();
    }

    /// <summary>
    /// Full step description: FAIL rows lead with the mismatch, then the step title, then the passing checks;
    /// other rows start with the title. NOT TESTED rows keep the recorded text (action | reason).
    /// summarizeLists (chat views): order/sequence lists read "Name column order changed: now A to Z (first: X)".
    /// </summary>
    public string Describe(RunStep step, string status, bool isTarget, bool withFieldNames = false, bool summarizeLists = false)
    {
        if (status == "NOT TESTED") return step.Description ?? "";
        var title = (string.IsNullOrWhiteSpace(step.Title) ? step.Step : step.Title).Trim().TrimEnd('.');
        var failed = step.Assertions.Where(a => !a.Passed).Select(a => FailClause(a, isTarget, summarizeLists)).ToList();
        var covered = step.Assertions.Where(a => a.Passed && a.Type is "CHANGES" or "STAYS SAME").Select(a => a.Field).ToHashSet(StringComparer.Ordinal);
        var passed = step.Assertions.Where(a => a.Passed).Select(a => PassClause(a, covered, summarizeLists)).Where(c => c != null).Cast<Clause>().ToList();

        var parts = new List<string>();
        if (status == "FAIL")
        {
            parts.AddRange(Join(failed, withFieldNames));
            var desc = step.Description ?? "";
            var abort = desc.IndexOf("ABORTED:", StringComparison.Ordinal);
            if (abort >= 0) parts.Insert(0, desc[abort..].Trim().TrimEnd('.') + ".");
            else if (failed.Count == 0) parts.Add("Step failed without a failed check (see the audit report).");
        }
        parts.Add(EndSentence(Capitalize(title)));
        parts.AddRange(Join(passed, withFieldNames));
        return string.Join(" ", parts);
    }
}

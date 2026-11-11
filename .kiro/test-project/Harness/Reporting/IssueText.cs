using System.Globalization;
using System.Text.RegularExpressions;

namespace MigrationTests.Reporting;

/// <summary>Page facts the Issue sentence needs: every planned CSS state (to say nothing when a group shows in all of them) and the WCAG minimum.</summary>
public sealed class IssueContext
{
    /// <summary>Planned CSS states of every module of the page (union).</summary>
    public IReadOnlyCollection<string> AllStates { get; init; } = Array.Empty<string>();
    /// <summary>VisualChecks.Severity.ContrastMin (WCAG AA 4.5:1).</summary>
    public double ContrastMin { get; init; } = 4.5;
}

/// <summary>
/// One short plain-English sentence per CSS group (the "Issue" column of every CSS table and the fixer task title).
/// Deterministic: templates per layer/property filled with the group's data (no LLM text). Elements are named by their
/// visible name and a plain role word ("The 'Active' checkbox", "Buttons (First Page, Next Page)"), grid cells by their
/// column header ("The Payor Name column cells (first 3 rows)"); colors by <see cref="ColorMath.Name"/>; px values as
/// "bigger/smaller by N px". The old/new page are always "old page (Reference)" / "new page (Target)".
/// The technical values stay in the other columns of the tables.
/// </summary>
public static class IssueText
{
    public const string OldPage = "old page (Reference)";
    public const string NewPage = "new page (Target)";

    /// <summary>Sentence subject: the text and whether the verb is plural.</summary>
    internal readonly record struct Subject(string Text, bool Plural)
    {
        /// <summary>Mid-sentence form ("the 'Active' label").</summary>
        public string Lower => LowerFirst(Text);
        public string V(string singular, string plural) => Plural ? plural : singular;
        public string Is => V("is", "are");
        public string Has => V("has", "have");
        public string Its => V("its", "their");
    }

    public static string For(CssGroup g, IReadOnlyList<CssDiffEntry> members, IssueContext? ctx = null)
    {
        ctx ??= new IssueContext();
        var s = What(g, members);
        var when = When(g.States, ctx.AllStates);
        var text = Sentence(g, members, s, when, ctx);
        if (g.AlsoIn.Count > 0) text += $" Also {JoinAnd(g.AlsoIn.Select(StateVerb).ToList())}.";
        return text;
    }

    // ───────────────────────────── subject ─────────────────────────────

    private static readonly Regex CellLabel = new(@"^(?<role>\w+) cells(, columns? [\d, ]+)? \(first (?<rows>\d+) rows\)$", RegexOptions.CultureInvariant);
    private static readonly Regex RoleName = new(@"^(?<role>[a-z]+) '(?<name>.*)'$", RegexOptions.CultureInvariant);
    private static readonly Regex RoleTag = new(@"^(?<role>[a-z]+) \((?<tag>[^)]*)\)$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> Nouns = new(StringComparer.Ordinal)
    {
        ["button"] = "button", ["columnheader"] = "column header", ["rowheader"] = "row header", ["gridcell"] = "cell", ["cell"] = "cell",
        ["combobox"] = "drop-down", ["listbox"] = "list", ["textbox"] = "text box", ["searchbox"] = "search box", ["spinbutton"] = "number box",
        ["checkbox"] = "checkbox", ["radio"] = "radio button", ["switch"] = "switch", ["label"] = "label", ["heading"] = "heading",
        ["link"] = "link", ["img"] = "image", ["image"] = "image", ["generic"] = "text", ["status"] = "status message", ["alert"] = "alert message",
        ["tab"] = "tab", ["tablist"] = "tab bar", ["row"] = "grid row", ["grid"] = "grid", ["table"] = "table", ["navigation"] = "navigation bar",
        ["dialog"] = "dialog", ["menu"] = "menu", ["menuitem"] = "menu item", ["option"] = "option", ["paragraph"] = "paragraph",
        ["banner"] = "page header", ["contentinfo"] = "page footer", ["main"] = "main area", ["form"] = "form", ["region"] = "section",
        ["progressbar"] = "progress bar", ["slider"] = "slider", ["tooltip"] = "tooltip", ["list"] = "list", ["listitem"] = "list item"
    };

    internal static string Noun(string? role)
    {
        var r = (role ?? "").Trim().ToLowerInvariant();
        if (r.Length == 0) return "element";
        return Nouns.TryGetValue(r, out var n) ? n : r;
    }

    internal static string PluralNoun(string noun)
    {
        if (noun.EndsWith("box", StringComparison.Ordinal) || noun.EndsWith("switch", StringComparison.Ordinal)) return noun + "es";
        if (noun == "text") return "texts";
        return noun.EndsWith('s') ? noun : noun + "s";
    }

    private static (string? Role, string Name) Parse(string label)
    {
        var m = RoleName.Match(label);
        if (m.Success) return (m.Groups["role"].Value, m.Groups["name"].Value);
        m = RoleTag.Match(label);
        if (m.Success && Nouns.ContainsKey(m.Groups["role"].Value)) return (m.Groups["role"].Value, "");
        return (null, label);
    }

    /// <summary>"The 'Active' checkbox", "Buttons (First Page, Next Page and 2 more)", "The Payor Name column cells (first 3 rows)", "The page".</summary>
    internal static Subject What(CssGroup g, IReadOnlyList<CssDiffEntry> members)
    {
        var elements = g.Elements.Count > 0 ? g.Elements : members.Select(m => m.Element).Distinct(StringComparer.Ordinal).ToList();
        if (elements.Count == 0) return new Subject("The element", false);
        if (elements.All(e => e == "(page)")) return new Subject("The page", false);

        var cells = elements.Select(e => CellLabel.Match(e)).ToList();
        if (cells.All(c => c.Success))
        {
            var rows = cells[0].Groups["rows"].Value;
            var cols = members.SelectMany(m => m.Columns ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
            return new Subject(CellsText(cols, rows), true);
        }

        var count = Math.Max(g.ElementCount, elements.Count);
        var parsed = elements.Select(Parse).ToList();
        if (count == 1)
        {
            var (role, name) = parsed[0];
            if (role == null) return new Subject("The " + MapLower(MapName(name)), false);
            var noun = Noun(role);
            if (name.Length == 0) return new Subject($"An unnamed {noun}", false);
            return new Subject($"The '{name}' {noun}", false);
        }
        var roles = parsed.Select(p => p.Role).Distinct().ToList();
        var nounRole = roles.Count == 1 && roles[0] != null ? roles[0]
            : !g.RoleClass.StartsWith("el:", StringComparison.Ordinal) && g.RoleClass.Length > 0 ? g.RoleClass : null;
        var plural = nounRole == null ? "Elements" : Capitalize(PluralNoun(Noun(nounRole)));
        var names = parsed.Select(p => p.Name).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var more = count - Math.Min(4, names.Count);
        if (names.Count == 0) return new Subject($"{count} {LowerFirst(plural)}", true);
        var shown = string.Join(", ", names.Take(4));
        return new Subject($"{plural} ({shown}{(more > 0 ? $" and {more} more" : "")})", true);
    }

    /// <summary>Cells subject from the resolved column headers ("column N" when a header was not found).</summary>
    internal static string CellsText(IReadOnlyList<string> columns, string rows)
    {
        var tail = $" (first {rows} rows)";
        if (columns.Count == 0) return "Grid cells" + tail;
        var resolved = columns.All(c => !c.StartsWith("column ", StringComparison.Ordinal));
        if (columns.Count > 3)
            return $"Cells in {columns.Count} columns ({string.Join(", ", columns.Take(3))} and {columns.Count - 3} more){tail}";
        if (resolved) return $"The {JoinAnd(columns)} column cells{tail}";
        return $"The cells in {JoinAnd(columns)}{tail}";
    }

    // ───────────────────────────── when ─────────────────────────────

    private static readonly Dictionary<string, (string Phrase, string Short)> StateWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["page_load"] = ("when the page first loads", "page load"),
        ["sorted"] = ("after sorting", "sorted"),
        ["filter_initial"] = ("before a filter is applied", "filter panel"),
        ["filtered"] = ("after filtering", "filtered"),
        ["filter_cleared"] = ("after clearing the filter", "filter cleared"),
        ["cell_edit_open"] = ("while a cell is being edited", "cell editing"),
        ["dirty"] = ("when there are unsaved changes", "unsaved changes"),
        ["after_save"] = ("after saving", "after save"),
        ["after_cancel"] = ("after cancelling changes", "after cancel"),
        ["paginated"] = ("after changing the page", "paging"),
        ["empty"] = ("when the grid is empty", "empty grid"),
        ["dialog_open"] = ("while a dialog is open", "dialog open"),
        ["validation_error"] = ("when a validation error shows", "validation error")
    };

    private static readonly Regex PageState = new(@"^S?_?p(?:age)?_?(?<n>\d+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static string StatePhrase(string state)
    {
        if (StateWords.TryGetValue(state, out var w)) return w.Phrase;
        var m = PageState.Match(state);
        if (m.Success) return $"on page {m.Groups["n"].Value}";
        return $"in the '{Humanize(state)}' state";
    }

    internal static string StateShort(string state)
    {
        if (StateWords.TryGetValue(state, out var w)) return w.Short;
        var m = PageState.Match(state);
        return m.Success ? $"page {m.Groups["n"].Value}" : Humanize(state);
    }

    private static string Humanize(string token) => Regex.Replace(token.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();

    /// <summary>" while a cell is being edited", " after sorting and after filtering", " in 5 page states (page load, sorted, filtered, ...)"; "" when every planned state shows it.</summary>
    internal static string When(IReadOnlyList<string> states, IReadOnlyCollection<string> allStates)
    {
        var st = states.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();
        if (st.Count == 0) return "";
        if (allStates.Count > 0 && allStates.All(a => st.Contains(a, StringComparer.Ordinal))) return "";
        if (st.Count == 1) return " " + StatePhrase(st[0]);
        if (st.Count == 2) return $" {StatePhrase(st[0])} and {StatePhrase(st[1])}";
        return $" in {st.Count} page states ({string.Join(", ", st.Take(3).Select(StateShort))}{(st.Count > 3 ? ", ..." : "")})";
    }

    private static string StateVerb(string prefix) => prefix switch
    {
        "hover" => "when hovered", "focus" => "when focused", "disabled" => "when disabled", _ => "in the " + prefix + " state"
    };

    // ───────────────────────────── predicates ─────────────────────────────

    private static string Sentence(CssGroup g, IReadOnlyList<CssDiffEntry> members, Subject s, string when, IssueContext ctx)
    {
        var prop = (g.Property ?? "").Trim();
        var r = g.Reference ?? "";
        var t = g.Target ?? "";

        switch (prop)
        {
            case AutoCssComparer.MissingProperty:
            case "(element)":
                return $"{s.Text} {s.V("exists", "exist")} on the {OldPage} but not on the {NewPage}{when}.";
            case AutoCssComparer.ExtraProperty:
                return $"{s.Text} {s.V("appears", "appear")} on the {NewPage} but not on the {OldPage}{when}. Confirm {s.V("it is", "they are")} intended.";
            case AutoCssComparer.LayoutTextMissingProperty:
                return $"{s.Text} (in a layout table on the {OldPage}) {s.V("was", "were")} not found in any compared element of the {NewPage}{when}. " +
                       $"Check that the new page shows {s.V("it", "them")}; not counted, because plain text outside compared elements is not checked.";
            case AutoCssComparer.LayoutTextExtraProperty:
                return $"{s.Text} (in a layout table on the {NewPage}) {s.V("was", "were")} not found in any compared element of the {OldPage}{when}. " +
                       $"Confirm {s.V("it is", "they are")} intended; not counted.";
            case "(unmeasured)":
                return $"{s.Text} could not be measured{when}: {s.Its} selector found nothing on the {OldPage}, so {s.Its} size was not compared.";
            case "(not captured on Target)":
                return $"The {NewPage} was not captured{when}, so its styling was not compared.";
            case "(css-map.json missing)":
            case "(unmapped)":
                return $"Styling was not compared{when}: there is no styling map (state-map/css-map.json) for this state.";
        }
        if (prop.StartsWith("(unmeasured: needs role ", StringComparison.Ordinal))
            return $"{s.Text} could not be measured{when}: state-map/css-map.json needs an element with role '{prop[("(unmeasured: needs role ".Length)..^1]}' in this state.";

        if (prop.StartsWith("text.", StringComparison.Ordinal))
        {
            var kind = prop["text.".Length..] switch { "name" => "accessible name", "label" => "label", "placeholder" => "placeholder", var k => k };
            return $"{s.Text} {s.Has} a different {kind}{when}: {QuoteOrNothing(r)} on the {OldPage} and {QuoteOrNothing(t)} on the {NewPage}.";
        }
        if (prop.StartsWith("responsive.", StringComparison.Ordinal)) return Responsive(g, s, prop, r, t, when);
        if (prop.StartsWith(ModuleReportGenerator.LayoutPrefix, StringComparison.Ordinal)) return Layout(g, s, prop, r, t, when);
        if (prop.StartsWith("(", StringComparison.Ordinal))
            return $"{s.Text} {s.V("differs", "differ")}{when}: {Value(r)} on the {OldPage} and {Value(t)} on the {NewPage}.";
        return Style(members, s, prop, r, t, when, ctx);
    }

    private static string QuoteOrNothing(string v) => string.IsNullOrWhiteSpace(v) ? "nothing" : $"'{v.Trim()}'";

    /// <summary>Value text with "role 'name'" labels made plain ("checkbox 'Active'" -> "the 'Active' checkbox").</summary>
    internal static string Value(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "nothing";
        return Regex.Replace(v.Trim(), @"\b(?<role>[a-z]+) '(?<name>[^']*)'", m =>
            Nouns.ContainsKey(m.Groups["role"].Value) ? $"the '{m.Groups["name"].Value}' {Noun(m.Groups["role"].Value)}" : m.Value);
    }

    private static string Layout(CssGroup g, Subject s, string prop, string r, string t, string when)
    {
        var metric = prop[ModuleReportGenerator.LayoutPrefix.Length..];
        switch (metric)
        {
            case "lines":
                return $"{s.Text} {s.V("wraps", "wrap")} onto {t} on the {NewPage} instead of {r} on the {OldPage}{when}.";
            case "overflow":
                return Clean(t)
                    ? $"{s.Text} {s.V("does", "do")} not fit on the {OldPage}: text is cut off there but fits on the {NewPage}{when}."
                    : $"{s.Text} {s.V("does", "do")} not fit on the {NewPage}: text is cut off{when}, while it fits on the {OldPage}.";
            case "hidden":
                return Clean(t)
                    ? $"{s.Text} {s.Is} {HiddenWord(r)} on the {OldPage}{when}; on the {NewPage} {s.V("it is", "they are")} fully visible."
                    : $"{s.Text} {s.Is} {HiddenWord(t)} on the {NewPage}{when}; on the {OldPage} {s.V("it is", "they are")} fully visible.";
            case "overlap":
            {
                var m = Regex.Match(t, @"^overlaps (?<other>.+) by (?<by>.+)$");
                var other = m.Success ? Value(m.Groups["other"].Value) : "";
                // A css-map name ("Payor Name filter input") reads as "the Payor Name filter input".
                if (m.Success && other == m.Groups["other"].Value.Trim() && other != "nothing")
                    other = "the " + MapLower(MapName(other));
                // Same role and name as the subject (e.g. a wrapper and its input): "another 'Active' checkbox".
                if (other.StartsWith("the ", StringComparison.Ordinal) && string.Equals(other, s.Lower, StringComparison.OrdinalIgnoreCase))
                    other = "another " + other["the ".Length..];
                return m.Success
                    ? $"{s.Text} {s.V("overlaps", "overlap")} {other} by {m.Groups["by"].Value} on the {NewPage}{when}; on the {OldPage} they do not overlap."
                    : $"{s.Text} {s.V("overlaps", "overlap")} another element on the {NewPage}{when}: {Value(t)} (old page: {Value(r)}).";
            }
            case "order":
                return $"{s.Text} {s.Is} in a different reading order{when}: {r} on the {OldPage}, {t} on the {NewPage}.";
            case "align":
                return $"{s.Text} {s.Is} aligned differently{when}: {Value(r)} on the {OldPage}, {Value(t)} on the {NewPage}.";
            case "labelPosition":
                return $"{s.Text} {s.V("sits", "sit")} {r} on the {OldPage} but {t} on the {NewPage}{when}.";
            case "spacing":
                if (TryPx(r, out var rs) && TryPx(t, out var ts))
                {
                    var d = Px(Math.Abs(ts - rs));
                    return ts > rs
                        ? $"The space before {s.Lower} is bigger by {d} on the {NewPage}{when} ({r} on the {OldPage} vs {t})."
                        : $"The space before {s.Lower} is smaller by {d} on the {NewPage}{when} ({r} on the {OldPage} vs {t}). {ReferenceLargerTail(g, "space")}";
                }
                break;
        }

        // Geometry metrics of css-map role elements.
        var (what, between) = metric switch
        {
            "gapToPager" => ("gap", "between the last grid row and the pager"),
            "gapToGrid" => ("gap", "between the filter panel and the grid"),
            "height" => ("grid height", ""),
            "rowHeight" => ("row height", ""),
            "headerHeight" => ("header row height", ""),
            _ => (FieldLabels.PropertyText(prop), "")
        };
        if (TryPx(r, out var rv) && TryPx(t, out var tv))
        {
            var d = Px(Math.Abs(tv - rv));
            var subject = between.Length > 0 ? $"the {what} {between}" : $"the {what}";
            if (rv > tv)
            {
                if (what == "gap" && g.Severity == "HIGH")
                    return $"The {OldPage} has a large empty gap ({r}) {between}; the {NewPage} has {t}{when}. This is an old-page defect; do not copy it.";
                return $"{Capitalize(subject)} is smaller on the {NewPage} by {d} ({r} on the {OldPage} vs {t}){when}. {ReferenceLargerTail(g, what == "gap" ? "space" : "height")}";
            }
            return $"{Capitalize(subject)} is bigger on the {NewPage} by {d} ({t} vs {r} on the {OldPage}){when}.";
        }
        return $"{Capitalize(FieldLabels.PropertyText(prop))} of {s.Lower} differs{when}: {Value(r)} on the {OldPage} and {Value(t)} on the {NewPage}.";
    }

    /// <summary>Reference larger (px): the Target must not be padded to match.</summary>
    private static string ReferenceLargerTail(CssGroup g, string what) =>
        g.Severity == "HIGH"
            ? $"The old page has much more {what}; treat it as an old-page defect and do not copy it."
            : $"Check the old page first; do not add extra {what} to the new page just to match.";

    private static bool Clean(string v) => v == "fits" || v.StartsWith("fits ", StringComparison.Ordinal) || v is "fully visible" or "visible";

    private static string HiddenWord(string v) => v switch
    {
        "clipped by its container" => "partly cut off (clipped by its container)",
        "hidden" => "not shown",
        _ => v
    };

    private static string Responsive(CssGroup g, Subject s, string prop, string r, string t, string when)
    {
        var parts = prop.Split('.');
        var width = parts.Length > 1 ? parts[1] : "";
        var kind = parts.Length > 2 ? parts[2] : "";
        var win = int.TryParse(width, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w < 1024
            ? $"At a narrow window ({width}px)" : $"At a {width}px wide window";
        var page = s.Text == "The page";
        var refWorse = g.ReferenceWorse || (Clean(t) && !Clean(r) && kind is "overflow" or "hidden");
        const string NothingToFix = "This is an old-page problem; nothing to fix on the new page.";
        switch (kind)
        {
            case "overflow":
                if (page)
                    return refWorse
                        ? $"{win} the {OldPage} scrolls sideways{Paren(r)} but the {NewPage} fits{when}. {NothingToFix}"
                        : $"{win} the {NewPage} scrolls sideways{Paren(t)} but the {OldPage} fits{when}.";
                return refWorse
                    ? $"{win} {s.Lower} {s.Is} cut off on the {OldPage} only{when}. {NothingToFix}"
                    : $"{win} {s.Lower} {s.Is} cut off on the {NewPage}{when}; on the {OldPage} {s.V("it fits", "they fit")}.";
            case "hidden":
                return refWorse
                    ? $"{win} {s.Lower} {s.Is} {HiddenWord(r)} on the {OldPage} only{when}. {NothingToFix}"
                    : $"{win} {s.Lower} {s.Is} {HiddenWord(t)} on the {NewPage}{when}; on the {OldPage} {s.V("it is", "they are")} {(r == "visible" ? "shown" : "fully visible")}.";
            case "lines":
                return $"{win} {s.Lower} {s.V("wraps", "wrap")} onto {t} on the {NewPage} instead of {r} on the {OldPage}{when}.";
        }
        return $"{win} {s.Lower} {s.V("differs", "differ")}{when}: {Value(r)} on the {OldPage} and {Value(t)} on the {NewPage}.";
    }

    /// <summary>"scrolls horizontally (1884px wide)" -> " (1884px wide)".</summary>
    private static string Paren(string v)
    {
        var m = Regex.Match(v, @"\((?<x>[^)]*)\)");
        return m.Success ? $" ({m.Groups["x"].Value})" : "";
    }

    // ───────────────────────────── style properties ─────────────────────────────

    private static string Style(IReadOnlyList<CssDiffEntry> members, Subject s, string prop, string r, string t, string when, IssueContext ctx)
    {
        var dot = prop.IndexOf('.');
        var prefix = dot > 0 && prop[..dot] is "hover" or "focus" or "disabled" ? prop[..dot] : "";
        var baseProp = prefix.Length > 0 ? prop[(dot + 1)..] : prop;
        var state = prefix.Length > 0 ? " " + StateVerb(prefix) : "";
        var lp = baseProp.ToLowerInvariant();

        // Merged border sides: "border", "border (top, bottom)", or one side "border-top".
        var border = BorderSide(lp);
        if (border != null)
            return $"The {border} of {s.Lower} differs{state}{when}: {BorderWords(r, border)} on the {OldPage} and {BorderWords(t, border)} on the {NewPage}.";

        var colorKind = ColorKind(lp);
        if (colorKind != null)
        {
            var nr = ColorMath.Name(r);
            var nt = ColorMath.Name(t);
            if (nr != null && nt != null)
            {
                var text = nr != nt
                    ? $"{s.Text} {s.V("looks", "look")} different{state}{when}: {colorKind} is {nr} on the {OldPage} and {nt} on the {NewPage}"
                    : $"{s.Text} {s.V("looks", "look")} slightly different{state}{when}: {colorKind} is a slightly different shade of {nr} on the {NewPage}";
                var drop = members.Where(m => m.ContrastReference is { } cr && m.ContrastTarget is { } ct && cr >= ctx.ContrastMin && ct < ctx.ContrastMin)
                    .Select(m => m.ContrastTarget!.Value).DefaultIfEmpty(-1).Min();
                if (drop >= 0) text += $" - harder to read (contrast {F(drop)}:1, below the {F(ctx.ContrastMin)}:1 minimum)";
                return text + ".";
            }
        }

        switch (lp)
        {
            case "font-weight":
                return $"{s.Text} {s.Is} {WeightWord(r)} on the {OldPage} and {WeightWord(t)} on the {NewPage}{state}{when}.";
            case "font-family":
            case "renderedfont":
                return $"{s.Text} {s.V("uses", "use")} a different font{state}{when}: {FirstFamily(r)} on the {OldPage} and {FirstFamily(t)} on the {NewPage}.";
            case "opacity":
                if (double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var ro) &&
                    double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var to))
                {
                    if (to == 0) return $"{s.Text} {s.Is} invisible (opacity 0) on the {NewPage}{state}{when}; on the {OldPage} the opacity is {r}.";
                    return $"{s.Text} {s.Is} {(to < ro ? "more" : "less")} transparent on the {NewPage}{state}{when} (opacity {r} on the {OldPage} vs {t}).";
                }
                break;
            case "text-align":
                return $"Text in {s.Lower} is aligned {Canon(prop, r)} on the {OldPage} and {Canon(prop, t)} on the {NewPage}{state}{when}.";
            case "box-shadow":
            {
                var rn = Canon(prop, r) == "none";
                var tn = Canon(prop, t) == "none";
                if (rn != tn) return $"{s.Text} {s.Has} a shadow on the {(rn ? NewPage : OldPage)} only{state}{when}.";
                return $"{s.Text} {s.Has} a different shadow{state}{when}.";
            }
            case "cursor":
                return $"Pointing at {s.Lower} shows {CursorWord(r)} on the {OldPage} and {CursorWord(t)} on the {NewPage}{state}{when}.";
        }

        var kind = SizeKind(lp);
        if (lp is "padding" or "margin" && Sides(r) is { } rs4 && Sides(t) is { } ts4 && !(TryPx(r, out _) && TryPx(t, out _)))
            return SidesSentence(Capitalize(kind), s, rs4, ts4, state + when);
        if (TryPx(r, out var rp) && TryPx(t, out var tp))
        {
            var d = Px(Math.Abs(tp - rp));
            return $"{Capitalize(kind)} of {s.Lower} is {(tp > rp ? "bigger" : "smaller")} by {d} on the {NewPage}{state}{when} ({r} on the {OldPage} vs {t}).";
        }
        return $"{Capitalize(kind)} of {s.Lower} differs{state}{when}: {Value(r)} on the {OldPage} and {Value(t)} on the {NewPage}.";
    }

    private static string Canon(string prop, string v) => StyleNormalizer.Canonical(prop, v);

    private static string? ColorKind(string p) => p switch
    {
        "color" => "text",
        "background-color" or "background" or "effectivebackground" => "background",
        "fill" or "stroke" => "icon color",
        "text-decoration-color" => "underline color",
        "outline-color" => "outline color",
        _ when p.StartsWith("border", StringComparison.Ordinal) && p.EndsWith("color", StringComparison.Ordinal) => "border color",
        _ when p.EndsWith("color", StringComparison.Ordinal) => FieldLabels.Humanize(p).ToLowerInvariant(),
        _ => null
    };

    /// <summary>"border" / "border (top, bottom)" / "border-top" / "outline" -> "border" / "top and bottom border" / "top border" / "outline".</summary>
    internal static string? BorderSide(string p)
    {
        if (p is "border" or "outline") return p;
        var m = Regex.Match(p, @"^border \((?<sides>[a-z, ]+)\)$");
        if (m.Success) return JoinAnd(m.Groups["sides"].Value.Split(',').Select(x => x.Trim()).ToList()) + " border";
        m = Regex.Match(p, @"^border-(?<side>top|right|bottom|left)$");
        return m.Success ? m.Groups["side"].Value + " border" : null;
    }

    /// <summary>"1px solid rgb(204, 204, 204)" -> "a 1px solid light gray line"; no border -> "no border".</summary>
    internal static string BorderWords(string v, string side)
    {
        var n = StyleNormalizer.Border(v);
        if (n == "none") return side == "outline" ? "no outline" : "no border";
        var tokens = Regex.Matches(n, @"rgba\([^)]*\)|\S+").Select(m => m.Value).ToList();
        var width = tokens.FirstOrDefault(x => x.EndsWith("px", StringComparison.Ordinal));
        var style = tokens.FirstOrDefault(x => x is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge" or "inset" or "outset");
        var color = tokens.Select(x => ColorMath.Name(x)).FirstOrDefault(x => x != null);
        var words = new[] { width, style, color }.Where(x => !string.IsNullOrEmpty(x)).ToList();
        return words.Count == 0 ? v : $"a {string.Join(" ", words)} line";
    }

    internal static string WeightWord(string v)
    {
        var c = StyleNormalizer.Canonical("font-weight", v);
        if (!double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) return $"font weight {v}";
        return w <= 300 ? "light" : w < 500 ? "normal weight" : w < 600 ? "medium weight" : w < 700 ? "semi-bold" : "bold";
    }

    private static string FirstFamily(string v)
    {
        var f = (v ?? "").Split(',').Select(x => x.Trim().Trim('"', '\'')).FirstOrDefault(x => x.Length > 0);
        return string.IsNullOrEmpty(f) ? "the default font" : f;
    }

    private static string CursorWord(string v) => v.Trim() switch
    {
        "pointer" => "a hand pointer",
        "default" => "an arrow",
        "auto" => "the default pointer",
        "not-allowed" => "a 'not allowed' sign",
        "text" => "a text cursor",
        "move" => "a move cursor",
        "wait" or "progress" => "a busy cursor",
        var x => $"the '{x}' cursor"
    };

    private static string SizeKind(string p) => p switch
    {
        "font-size" => "text size",
        "line-height" => "line height",
        "letter-spacing" => "letter spacing",
        "border-radius" => "corner rounding",
        "gap" or "column-gap" or "row-gap" => "gap between items",
        "width" => "width",
        "height" => "height",
        "min-height" => "minimum height",
        "min-width" => "minimum width",
        _ when p.StartsWith("padding", StringComparison.Ordinal) => "inner spacing (" + p + ")",
        _ when p.StartsWith("margin", StringComparison.Ordinal) => "outer spacing (" + p + ")",
        _ when p.StartsWith("border-", StringComparison.Ordinal) && p.EndsWith("-width", StringComparison.Ordinal) => "border width",
        _ => FieldLabels.PropertyText(p)
    };

    // ───────────────────────────── helpers ─────────────────────────────

    private static readonly Regex SinglePx = new(@"^\s*(-?\d*\.?\d+)px\s*$", RegexOptions.CultureInvariant);

    private static bool TryPx(string? v, out double px)
    {
        px = 0;
        var m = SinglePx.Match(v ?? "");
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out px);
    }

    private static readonly string[] SideNames = { "top", "right", "bottom", "left" };

    /// <summary>CSS box shorthand of 1-4 px values ("0px 2px") as top, right, bottom, left; null when not all px.</summary>
    internal static double[]? Sides(string? v)
    {
        var parts = (v ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return null;
        var px = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!TryPx(parts[i], out px[i])) return null;
        return parts.Length switch
        {
            1 => new[] { px[0], px[0], px[0], px[0] },
            2 => new[] { px[0], px[1], px[0], px[1] },
            3 => new[] { px[0], px[1], px[2], px[1] },
            _ => px
        };
    }

    /// <summary>"2px on every side", "0px top/bottom and 2px left/right", "top 1px, right 2px, bottom 3px, left 4px".</summary>
    internal static string SidesWords(double[] s)
    {
        if (s.All(x => x == s[0])) return $"{Px(s[0])} on every side";
        if (s[0] == s[2] && s[1] == s[3]) return $"{Px(s[0])} top/bottom and {Px(s[1])} left/right";
        return string.Join(", ", SideNames.Select((n, i) => $"{n} {Px(s[i])}"));
    }

    /// <summary>"... is bigger by 2px on the top and bottom on the new page (Target) (0px top/bottom and 2px left/right on the old page (Reference) vs 2px on every side)."</summary>
    private static string SidesSentence(string kind, Subject s, double[] r, double[] t, string tail)
    {
        var changes = Enumerable.Range(0, 4).Where(i => t[i] != r[i])
            .GroupBy(i => Math.Round(t[i] - r[i], 1))
            .Select(g =>
            {
                var sides = g.Select(i => SideNames[i]).ToList();
                var where = sides.Count == 4 ? "on every side" : "on the " + JoinAnd(sides);
                return $"{(g.Key > 0 ? "bigger" : "smaller")} by {Px(Math.Abs(g.Key))} {where}";
            }).ToList();
        var values = $"({SidesWords(r)} on the {OldPage} vs {SidesWords(t)})";
        if (changes.Count == 0) return $"{kind} of {s.Lower} is written differently{tail} but has the same size {values}.";
        if (changes.Count == 1) return $"{kind} of {s.Lower} is {changes[0]} on the {NewPage}{tail} {values}.";
        return $"{kind} of {s.Lower} differs on the {NewPage}{tail}: {JoinAnd(changes)} {values}.";
    }

    private static string Px(double v) => F(Math.Round(v, 1)) + "px";

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string JoinAnd(IReadOnlyList<string> w) =>
        w.Count switch { 0 => "", 1 => w[0], _ => string.Join(", ", w.Take(w.Count - 1)) + " and " + w[^1] };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Lower-cases the first letter unless the first word is an acronym ("ID", "URL").</summary>
    private static string LowerFirst(string s)
    {
        if (s.Length == 0) return s;
        var first = s.Split(' ', 2)[0];
        if (first.Length > 1 && first.All(c => !char.IsLetter(c) || char.IsUpper(c))) return s;
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    /// <summary>
    /// css-map name after "The": the first letter is lower-cased only when the name reads as a common phrase
    /// ("Grid pager" -> "grid pager"); names that start with a proper term keep their case ("Payor Name filter input").
    /// </summary>
    private static string MapLower(string name)
    {
        var words = name.Split(' ');
        if (words.Length > 1 && words[1].Length > 0 && char.IsUpper(words[1][0])) return name;
        return LowerFirst(name);
    }

    /// <summary>css-map element names: "ClearButton" -> "Clear button"; names with spaces stay as they are.</summary>
    private static string MapName(string name) =>
        name.Contains(' ') || !Regex.IsMatch(name, "[a-z][A-Z]") ? name : FieldLabels.Humanize(name);
}

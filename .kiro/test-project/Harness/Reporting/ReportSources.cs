using System.Text;
using System.Text.Json.Nodes;

namespace MigrationTests.Reporting;

/// <summary>
/// Read-only view of one page dir for ChatRenderer and FinalReportBuilder (design §5 data sources):
/// plan = state-map/plan.json, res(X,S) = runs/Module{X}/{S}/results.json, sum(X) = runs/Module{X}/summary.json,
/// bld(X) = runs/Module{X}/build.json, st = status.json, tm = timings.json, fx = fixes.json.
/// Missing files give null / empty values; corrupt files throw ReportInputException.
/// </summary>
internal sealed class ReportSources
{
    public string PageDir { get; }
    public PlanFile? Plan { get; }
    public JsonNode? Status { get; }
    public JsonNode? Timings { get; }
    public LifecycleTimings Lt { get; }
    public List<FixEntry> Fixes { get; }
    public DateTimeOffset Now { get; }

    private readonly Dictionary<string, ModuleSummary?> _summaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RunResultsFile?> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonNode?> _builds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModuleSummary?> _priorSummaries = new(StringComparer.OrdinalIgnoreCase);
    private CssMapFile? _cssMap;
    private bool _cssMapLoaded;

    private ReportSources(string pageDir, PlanFile? plan, JsonNode? status, DateTimeOffset now)
    {
        PageDir = pageDir;
        Plan = plan;
        Status = status;
        Now = now;
        Timings = ReportJson.ReadNode(Path.Combine(pageDir, "timings.json"));
        Lt = LifecycleTimings.FromNode(Timings, now);
        Fixes = ReportJson.Read<List<FixEntry>>(Path.Combine(pageDir, "fixes.json")) ?? new List<FixEntry>();
    }

    public static ReportSources Load(string pageDir, DateTimeOffset? now = null)
    {
        pageDir = Path.GetFullPath(pageDir);
        var plan = ReportJson.Read<PlanFile>(Path.Combine(pageDir, "state-map", "plan.json"));
        var status = ReportJson.ReadNode(Path.Combine(pageDir, "status.json"));
        return new ReportSources(pageDir, plan, status, now ?? DateTimeOffset.Now);
    }

    /// <summary>Uses already-loaded plan/status and freshly generated summaries (Generate ALL).</summary>
    public static ReportSources From(string pageDir, PlanFile? plan, JsonNode? status, IEnumerable<ModuleSummary> summaries, DateTimeOffset? now = null)
    {
        var d = new ReportSources(Path.GetFullPath(pageDir), plan, status, now ?? DateTimeOffset.Now);
        foreach (var s in summaries) d._summaries[s.Module] = s;
        return d;
    }

    public string Page =>
        Plan?.Page is { Length: > 0 } p ? p
        : LifecycleTimings.Str(Status?["page"]) is { Length: > 0 } s ? s
        : Path.GetFileName(PageDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>Versioned run id (status.session.id, e.g. 'run-20261028-101500'); null when no run started.</summary>
    public string? RunId => LifecycleTimings.Str(Status?["session"]?["id"]) is { Length: > 0 } r ? r : null;

    /// <summary>status.session.startedAt (local time); null when absent or unparsable.</summary>
    public DateTimeOffset? RunStartedAt =>
        DateTimeOffset.TryParse(LifecycleTimings.Str(Status?["session"]?["startedAt"]), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t) ? t : null;

    /// <summary>'Run run-20261028-101500 (started 28 Oct 10:15)', or null without a run.</summary>
    public string? RunLabel => RunId == null ? null
        : RunStartedAt is { } t ? $"Run {RunId} (started {t.ToString("dd MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture)})"
        : $"Run {RunId}";

    /// <summary>'Run id: run-... - started 2026-10-28 10:15:00 - plan revision 1' (first page of the documents).</summary>
    public string RunDetails
    {
        get
        {
            var parts = new List<string> { $"Run id: {RunId ?? "-"}" };
            if (RunStartedAt is { } t) parts.Add($"started {t:yyyy-MM-dd HH:mm:ss}");
            var rev = LifecycleTimings.Str(Status?["session"]?["planRevision"]);
            if (!string.IsNullOrEmpty(rev)) parts.Add($"plan revision {rev}");
            return string.Join(" - ", parts);
        }
    }

    /// <summary>Plan module ids in plan order, then any runs/Module* folder or loaded summary not in the plan.</summary>
    public List<string> ModuleIds()
    {
        var ids = new List<string>();
        foreach (var m in Plan?.Modules ?? new List<PlanModule>())
            if (m.Id.Length > 0 && !ids.Contains(m.Id, StringComparer.OrdinalIgnoreCase)) ids.Add(m.Id.ToUpperInvariant());
        var runs = Path.Combine(PageDir, "runs");
        var extra = new List<string>();
        if (Directory.Exists(runs))
            extra.AddRange(Directory.GetDirectories(runs, "Module*").Select(d => Path.GetFileName(d)!["Module".Length..]).Where(s => s.Length > 0));
        extra.AddRange(_summaries.Keys);
        foreach (var e in extra.OrderBy(x => x, StringComparer.Ordinal))
            if (!ids.Contains(e, StringComparer.OrdinalIgnoreCase)) ids.Add(e.ToUpperInvariant());
        return ids;
    }

    /// <summary>Planned module ids (plan order); falls back to <see cref="ModuleIds"/> without a plan.</summary>
    public List<string> PlannedIds() =>
        Plan != null && Plan.Modules.Count > 0 ? Plan.Modules.Select(m => m.Id.ToUpperInvariant()).ToList() : ModuleIds();

    public PlanModule? PlanModule(string x) =>
        Plan?.Modules.FirstOrDefault(m => m.Id.Equals(x, StringComparison.OrdinalIgnoreCase));

    public string ModuleName(string x) =>
        PlanModule(x)?.Name is { Length: > 0 } n ? n : LifecycleTimings.Str(Status?["modules"]?[x]?["name"]);

    public string RunDir(string x) => Path.Combine(PageDir, "runs", $"Module{x}");

    public ModuleSummary? Summary(string x)
    {
        if (!_summaries.TryGetValue(x, out var s))
            _summaries[x] = s = ReportJson.Read<ModuleSummary>(Path.Combine(RunDir(x), "summary.json"));
        return s;
    }

    public RunResultsFile? Results(string x, string system)
    {
        var key = x + "/" + system;
        if (!_results.TryGetValue(key, out var r))
            _results[key] = r = ReportJson.Read<RunResultsFile>(Path.Combine(RunDir(x), system, "results.json"));
        return r;
    }

    private List<CssGroup>? _cssGroups;
    private int? _chatTop;

    /// <summary>VisualChecks.Groups.ChatTop of appsettings.test.json (default 15): actionable groups shown in chat views.</summary>
    public int ChatTopGroups => _chatTop ??= Math.Max(1,
        MigrationTests.Playwright.VisualChecksSettings.Load(MigrationTests.Playwright.HarnessPaths.Resolve("appsettings.test.json")).Groups?.ChatTop ?? 15);

    /// <summary>
    /// Page-level CSS groups (runs/css_groups.json, written by ModuleReportGenerator). Without the file (older runs) the
    /// rows of the loaded summaries are grouped here (generic role classes).
    /// </summary>
    public List<CssGroup> CssGroups()
    {
        if (_cssGroups != null) return _cssGroups;
        var file = ReportJson.Read<CssGroupsFile>(Path.Combine(PageDir, "runs", "css_groups.json"));
        if (file != null)
        {
            // css_groups.json of an older run has no Issue text: build it from the member rows (summary cssDiffs[].groupKey).
            if (file.Groups.Any(g => string.IsNullOrEmpty(g.Issue)))
            {
                var all = ModuleIds().SelectMany(x => Summary(x)?.CssDiffs ?? new List<CssDiffEntry>()).ToList();
                var ctx = new IssueContext { AllStates = (Plan?.Modules ?? new List<PlanModule>()).SelectMany(m => m.CssStates).Distinct(StringComparer.Ordinal).ToList() };
                foreach (var g in file.Groups.Where(g => string.IsNullOrEmpty(g.Issue)))
                    g.Issue = IssueText.For(g, all.Where(c => c.GroupKey == g.Key).ToList(), ctx);
            }
            return _cssGroups = file.Groups;
        }
        var ids = ModuleIds();
        var rows = ids.SelectMany(x => (Summary(x)?.CssDiffs ?? new List<CssDiffEntry>()).Select(c => (x, c))).ToList();
        var issue = new IssueContext
        {
            AllStates = (Plan?.Modules ?? new List<PlanModule>()).SelectMany(m => m.CssStates).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList()
        };
        return _cssGroups = CssGrouper.Build(rows, ids, null, null, issue).Groups;
    }

    /// <summary>state-map/css-map.json (null when missing).</summary>
    public CssMapFile? CssMap
    {
        get
        {
            if (!_cssMapLoaded)
            {
                _cssMap = ReportJson.Read<CssMapFile>(Path.Combine(PageDir, "state-map", "css-map.json"));
                _cssMapLoaded = true;
            }
            return _cssMap;
        }
    }

    /// <summary>
    /// Planned or captured CSS states of module X that have no css-map.json element, so nothing was compared
    /// for them. Empty when css-map.json is missing (the generator then reports '(unmapped)' diffs instead).
    /// </summary>
    public List<string> UncomparedCssStates(string x)
    {
        var map = CssMap;
        if (map == null) return new List<string>();
        var mapped = new HashSet<string>(map.Elements.Select(e => e.State), StringComparer.Ordinal);
        var states = new List<string>();
        foreach (var s in (PlanModule(x)?.CssStates ?? new List<string>())
                     .Concat(Results(x, "Reference")?.CssStatesCaptured ?? new List<string>())
                     .Concat(Results(x, "Target")?.CssStatesCaptured ?? new List<string>()))
            if (!string.IsNullOrWhiteSpace(s) && !states.Contains(s)) states.Add(s);
        return states.Where(s => !mapped.Contains(s)).ToList();
    }

    /// <summary>
    /// The finding as it was before fix round <paramref name="round"/> (runs/Module{X}/summary.before-round{N}.json,
    /// written by rerun-target), searching round N down to 0. Null when no snapshot has it.
    /// </summary>
    public Finding? PriorFinding(string x, int round, string id)
    {
        for (var r = round; r >= 0; r--)
        {
            var key = $"{x}/{r}";
            if (!_priorSummaries.TryGetValue(key, out var s))
                _priorSummaries[key] = s = ReportJson.Read<ModuleSummary>(Path.Combine(RunDir(x), $"summary.before-round{r}.json"));
            var f = s?.Findings.FirstOrDefault(f => f.Id == id);
            if (f != null) return f;
        }
        return null;
    }

    public JsonNode? Build(string x)
    {
        if (!_builds.TryGetValue(x, out var b))
            _builds[x] = b = ReportJson.ReadNode(Path.Combine(RunDir(x), "build.json"));
        return b;
    }

    /// <summary>status.modules[X].harnessError (null when absent or JSON null).</summary>
    public string? HarnessError(string x)
    {
        var s = LifecycleTimings.Str(Status?["modules"]?[x]?["harnessError"]);
        return s.Length == 0 || s == "null" ? null : s;
    }

    /// <summary>"read-only" | "mutating" | null (missing/invalid → scheduled as mutating).</summary>
    public string? DataEffectRaw(string x)
    {
        var v = PlanModule(x)?.DataEffect?.Trim().ToLowerInvariant();
        return v is "read-only" or "mutating" ? v : null;
    }

    public string DataEffectLabel(string x) => DataEffectRaw(x) ?? "mutating (default: not set in plan)";

    /// <summary>status.fixLoop.maxRounds clamped to 2 (design §3.7).</summary>
    public int MaxRounds
    {
        get
        {
            var m = (int)LifecycleTimings.Long(Status?["fixLoop"]?["maxRounds"], 2);
            return m <= 0 || m > 2 ? 2 : m;
        }
    }

    public int FixRound => (int)LifecycleTimings.Long(Status?["fixLoop"]?["round"], 0);

    private FieldLabels? _labels;

    /// <summary>plan.json fieldLabels with the humanizing fallback (P7).</summary>
    public FieldLabels Labels => _labels ??= new FieldLabels(Plan?.FieldLabels);
}

/// <summary>Markdown helpers for the chat views (design §5 common rules).</summary>
internal static class Md
{
    /// <summary>Chat cell cap (about 200 characters, cut cleanly); the md audit reports keep full values.</summary>
    public const int DescriptionCap = 200;
    /// <summary>CSS Issue sentence cap: a whole sentence plus its "old-page defect" / impact clause is never cut.</summary>
    public const int IssueCap = 400;
    public const int ValueCap = 80;
    public const int CodeCap = 60;
    public const int MaxRows = 25;

    /// <summary>
    /// Newlines → space, capped, then '|' escaped as '\|' (P4: the cut never splits an escape).
    /// A cap cuts at the last sentence end ('. ') when there is one in the second half, else at the last word
    /// boundary; never mid-word and never with a trailing '...'.
    /// </summary>
    public static string Cell(string? s, int cap = ValueCap)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if (t.Length > cap) t = CleanCut(t, cap);
        return Escape(t);
    }

    /// <summary>Cuts at a sentence end, else a word boundary (else hard at the cap), and trims trailing separators.</summary>
    internal static string CleanCut(string t, int cap)
    {
        if (t.Length <= cap) return t;
        var cut = t.LastIndexOf(". ", Math.Min(cap, t.Length - 1), StringComparison.Ordinal);
        if (cut >= cap / 2) return t[..(cut + 1)];
        var sp = t.LastIndexOf(' ', cap);
        var r = sp >= cap / 2 ? t[..sp] : t[..cap];
        return r.TrimEnd(' ', ',', ';', ':', '-', '(').TrimEnd('.').TrimEnd();
    }

    /// <summary>
    /// A filesystem path as inline code, so markdown never treats '\.' or '\_' in a Windows path as an escape
    /// (which dropped the backslash before '.kiro' in chat).
    /// </summary>
    public static string PathCode(string p) => "`" + p + "`";

    /// <summary>First cell of an empty table's only row (plain English instead of a bare "(none)").</summary>
    public const string EmptyRow = "None found.";

    /// <summary>Escapes '|' as '\|' so a cell never adds a column.</summary>
    public static string Escape(string s) => s.Replace("|", "\\|");

    /// <summary>Splits a markdown table row on '|' not preceded by '\' and unescapes '\|' (outer pipes dropped).</summary>
    public static List<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal)) t = t[..^1];
        var cur = new StringBuilder();
        for (var i = 0; i < t.Length; i++)
        {
            var c = t[i];
            if (c == '\\' && i + 1 < t.Length && t[i + 1] == '|') { cur.Append('|'); i++; continue; }
            if (c == '|') { cells.Add(cur.ToString().Trim()); cur.Clear(); continue; }
            cur.Append(c);
        }
        cells.Add(cur.ToString().Trim());
        return cells;
    }

    public static string Row(params string[] cells) => "| " + string.Join(" | ", cells) + " |";

    public static string Header(params string[] cols) =>
        Row(cols) + Environment.NewLine + "|" + string.Concat(Enumerable.Repeat("---|", cols.Length));

    /// <summary>
    /// Appends a blank line (unless the builder already ends with one), header + rows, and a blank line after.
    /// Empty → one '<see cref="EmptyRow"/>' row. With a cap, rows beyond <see cref="MaxRows"/> become one
    /// '| ... | N more in &lt;reportPath&gt; | | ...' row padded to the column count.
    /// </summary>
    public static void Table(StringBuilder sb, string[] cols, IReadOnlyList<string[]> rows, bool capped, string reportPath, string[]? footer = null)
    {
        EnsureBlankLine(sb);
        sb.AppendLine(Header(cols));
        if (rows.Count == 0)
        {
            sb.AppendLine(Row(Pad(new[] { EmptyRow }, cols.Length)));
        }
        else
        {
            var take = capped && rows.Count > MaxRows ? MaxRows : rows.Count;
            for (var i = 0; i < take; i++) sb.AppendLine(Row(Pad(rows[i], cols.Length)));
            if (take < rows.Count) sb.AppendLine(Row(Pad(new[] { "...", $"{rows.Count - take} more in {PathCode(reportPath)}" }, cols.Length)));
            if (footer != null) sb.AppendLine(Row(Pad(footer, cols.Length)));
        }
        sb.AppendLine();
    }

    /// <summary>Pads (or, defensively, merges the tail of) a row to exactly <paramref name="n"/> cells.</summary>
    public static string[] Pad(string[] cells, int n)
    {
        if (cells.Length == n) return cells;
        var r = new string[n];
        for (var i = 0; i < n; i++) r[i] = i < cells.Length ? cells[i] : "";
        if (cells.Length > n) r[n - 1] = string.Join(" / ", cells.Skip(n - 1));
        return r;
    }

    /// <summary>Appends a blank line unless the text is empty or already ends with one.</summary>
    public static void EnsureBlankLine(StringBuilder sb)
    {
        if (sb.Length == 0) return;
        var nl = Environment.NewLine;
        if (!sb.ToString().EndsWith(nl, StringComparison.Ordinal)) sb.AppendLine();
        if (!sb.ToString().EndsWith(nl + nl, StringComparison.Ordinal)) sb.AppendLine();
    }

    /// <summary>One-line purpose sentence of a section (its own paragraph: a blank line follows).</summary>
    public static void Purpose(StringBuilder sb, string text)
    {
        EnsureBlankLine(sb);
        sb.AppendLine(text);
        sb.AppendLine();
    }

    /// <summary>'### title' followed by a blank line.</summary>
    public static void Heading(StringBuilder sb, string title)
    {
        EnsureBlankLine(sb);
        sb.AppendLine(title);
        sb.AppendLine();
    }

    public static string Seconds(long ms) => (ms / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";

    public static int SevRank(string s) => s switch { "HIGH" => 0, "MEDIUM" => 1, "LOW" => 2, _ => 3 };
}

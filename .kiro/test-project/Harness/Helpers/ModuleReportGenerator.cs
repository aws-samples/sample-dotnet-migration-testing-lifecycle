using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Text.RegularExpressions;
using MigrationTests.Playwright;

namespace MigrationTests.Reporting;

// ═══════════════════════════════════════════════════════════════════════
// FILE MODELS (design §5.4, §6, §7). Shared by ModuleTestRunner (writer) and
// ModuleReportGenerator (reader) so the schema cannot drift.
// Serialized camelCase via ReportJson.Options.
// ═══════════════════════════════════════════════════════════════════════

public sealed class RunResultsFile
{
    public int SchemaVersion { get; set; } = 1;
    public string Page { get; set; } = "";
    public string Module { get; set; } = "";
    public string System { get; set; } = "";
    public string Test { get; set; } = "";
    public string RunId { get; set; } = "";
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public bool Aborted { get; set; }
    public string? AbortReason { get; set; }
    public bool CssMapMissing { get; set; }
    public RunTotals Totals { get; set; } = new();
    public List<RunStep> Steps { get; set; } = new();
    public List<RunCapture> Captures { get; set; } = new();
    public List<string> UncoveredHtmlDiffs { get; set; } = new();
    public List<string> CssStatesCaptured { get; set; } = new();
}

public sealed class RunTotals
{
    public int StepsPass { get; set; }
    public int StepsFail { get; set; }
    public int StepsNotTested { get; set; }
    public int AssertionsTotal { get; set; }
    public int AssertionsPassed { get; set; }
    public int AssertionsFailed { get; set; }
}

public sealed class RunStep
{
    public string Step { get; set; } = "";
    public string Status { get; set; } = "FAIL";
    public string Description { get; set; } = "";
    /// <summary>Short step title (the ExecuteStep description argument). Extra field, not in §5.4.</summary>
    public string Title { get; set; } = "";
    public long ActionMs { get; set; }
    public long StepMs { get; set; }
    public List<RunAssertion> Assertions { get; set; } = new();
    /// <summary>Screenshot after the step, relative to the page dir (runs/Module{X}/{System}/screenshots/{step}.jpg); null when not captured.</summary>
    public string? Screenshot { get; set; }
    /// <summary>Content sanity check hits of this step (leaked syntax in the visible text, console/page errors). Never change Status.</summary>
    public List<ContentIssue> ContentIssues { get; set; } = new();
}

/// <summary>One content sanity check hit on a step snapshot.</summary>
public sealed class ContentIssue
{
    public const string LeakedSyntax = "leaked-syntax";
    public const string ConsoleError = "console-error";
    public const string PageError = "page-error";
    public const string BrokenImage = "broken-image";
    public const string FailedRequest = "failed-request";
    public const string EmptySection = "empty-section";
    public const string Accessibility = "a11y";

    /// <summary>Every kind in report order.</summary>
    public static readonly string[] AllKinds = { LeakedSyntax, BrokenImage, FailedRequest, ConsoleError, PageError, EmptySection, Accessibility };

    /// <summary>leaked-syntax | console-error | page-error | broken-image | failed-request | empty-section | a11y.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Matched tokens (leaked-syntax only).</summary>
    public List<string> Tokens { get; set; } = new();
    /// <summary>Offending visible text line (120 chars around the first match, '...' marks a cut) or the error message (≤ 200 chars).</summary>
    public string Excerpt { get; set; } = "";
}

public sealed class RunAssertion
{
    public string Field { get; set; } = "";
    public string Type { get; set; } = "";
    public string Expected { get; set; } = "";
    public bool ExpectedIsConstant { get; set; }
    public string Actual { get; set; } = "";
    public bool Passed { get; set; }
    public string Message { get; set; } = "";
}

public sealed class RunCapture
{
    /// <summary>Executing step (step name, NAV or PRE).</summary>
    public string Step { get; set; } = "";
    /// <summary>State id passed to runner.Capture (S0, S_p2, ...).</summary>
    public string State { get; set; } = "";
    public string Field { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class CssMapFile
{
    public List<CssMapElement> Elements { get; set; } = new();
}

public sealed class CssMapElement
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Target { get; set; } = "";
    /// <summary>
    /// Optional layout role: grid | row | header | pager | filter. Drives the derived layout.* geometry rows
    /// (layout.height, layout.rowHeight, layout.headerHeight, layout.gapToPager, layout.gapToGrid).
    /// </summary>
    public string? Role { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
    public Dictionary<string, string> Accepted { get; set; } = new();
}

public sealed class PlanFile
{
    public string Page { get; set; } = "";
    public int Revision { get; set; }
    public List<PlanModule> Modules { get; set; } = new();
    /// <summary>Phase-0 states (S0, S_p2, ...); rendered by the plan chat view. Empty when absent.</summary>
    public List<PlanState> States { get; set; } = new();
    /// <summary>
    /// Field pattern → plain-English label for the step descriptions (P7), e.g. "row[{n}].name.text" →
    /// "Payor Name in row ID {n}". Placeholders: {n} (digits), {name} (any text without '.', '[' or ']').
    /// </summary>
    public Dictionary<string, string> FieldLabels { get; set; } = new();
}

public sealed class PlanModule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>One-sentence user story ("As a user I ...").</summary>
    public string UserStory { get; set; } = "";
    /// <summary>"read-only" or "mutating"; anything else is scheduled as mutating.</summary>
    public string DataEffect { get; set; } = "";
    public int PlannedSteps { get; set; }
    public string ReferenceTest { get; set; } = "";
    public string TargetTest { get; set; } = "";
    public List<string> CssStates { get; set; } = new();
    public List<PlanStep> Steps { get; set; } = new();
    public List<PlanNotTested> NotTested { get; set; } = new();
}

public sealed class PlanStep
{
    public string Id { get; set; } = "";
    public string Action { get; set; } = "";
    public List<string> Changes { get; set; } = new();
    public List<string> StaysSame { get; set; } = new();
    public List<string> Checks { get; set; } = new();
    /// <summary>Plain-English sentence per check, without observed values (P3). The plan view renders only this.</summary>
    public List<string> ChecksText { get; set; } = new();
}

public sealed class PlanState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string HowReached { get; set; } = "";
    public List<string> Indicators { get; set; } = new();
}

public sealed class PlanNotTested
{
    public string Item { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class FixEntry
{
    public int Round { get; set; }
    public string Source { get; set; } = "";
    public string Module { get; set; } = "";
    public List<string> FindingIds { get; set; } = new();
    public string Kind { get; set; } = "";
    public string File { get; set; } = "";
    public string Change { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string At { get; set; } = "";
}

public sealed class ModuleSummary
{
    public string Module { get; set; } = "";
    public string ReferenceStatus { get; set; } = "FAIL";
    public string TargetStatus { get; set; } = "FAIL";
    public int OpenFindings { get; set; }
    /// <summary>Actionable page-level CSS groups OWNED by this module (owner = first module in plan order the group was seen in).</summary>
    public int ActionableCssDiffs { get; set; }
    /// <summary>Raw actionable CSS rows of this module (informational; a module with rows never passes).</summary>
    public int ActionableCssRows { get; set; }
    public int CssDiffsTotal { get; set; }
    /// <summary>Ids (G1..Gn) of the page-level CSS groups that include rows of this module.</summary>
    public List<string> CssGroupsSeen { get; set; } = new();
    /// <summary>Ids of the page-level CSS groups owned by this module.</summary>
    public List<string> CssGroupsOwned { get; set; } = new();
    public int NotTested { get; set; }
    public List<Finding> Findings { get; set; } = new();
    public List<CssDiffEntry> CssDiffs { get; set; } = new();
    /// <summary>Target-only assertions and captures (LOW, never counted).</summary>
    public List<UnmatchedItem> Unmatched { get; set; } = new();
    /// <summary>Stack profile applied to the CSS rows (profiles/&lt;name&gt;.json; "generic" when none is configured).</summary>
    public string StackProfile { get; set; } = "";
    /// <summary>Content sanity check hits on the Reference (baseline issues). Listed in the reports, never counted in Target parity.</summary>
    public int ReferenceDefectCount { get; set; }
    public List<ReferenceDefect> ReferenceDefects { get; set; } = new();
    public string GeneratedAt { get; set; } = "";
}

/// <summary>A baseline problem found on the Reference by the content sanity checks (e.g. a leaked page directive).</summary>
public sealed class ReferenceDefect
{
    /// <summary>{Module}-REF{n}, numbered in Reference step order.</summary>
    public string Id { get; set; } = "";
    /// <summary>First step that showed it.</summary>
    public string Step { get; set; } = "";
    /// <summary>leaked-syntax | console-error | page-error.</summary>
    public string Kind { get; set; } = "";
    /// <summary>HIGH for leaked syntax, MEDIUM for console/page errors.</summary>
    public string Severity { get; set; } = "HIGH";
    public List<string> Tokens { get; set; } = new();
    public string Excerpt { get; set; } = "";
    /// <summary>Every step that showed it.</summary>
    public List<string> Steps { get; set; } = new();
}

public sealed class Finding
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Step { get; set; } = "";
    public string Field { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Target { get; set; } = "";
    public string Expected { get; set; } = "";
    public string Severity { get; set; } = "HIGH";
    public bool Actionable { get; set; } = true;
    public List<string> Sources { get; set; } = new();
    public List<MissingAssertion> MissingAssertions { get; set; } = new();
    public List<MissingCapture> MissingCaptures { get; set; } = new();
    /// <summary>Human-readable evidence lines (extra field).</summary>
    public List<string> Details { get; set; } = new();
}

public sealed class MissingAssertion
{
    public string Type { get; set; } = "";
    public string Expected { get; set; } = "";
}

public sealed class MissingCapture
{
    public string State { get; set; } = "";
    public string Field { get; set; } = "";
}

public sealed class CssDiffEntry
{
    public string Element { get; set; } = "";
    public string State { get; set; } = "";
    public string Property { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Target { get; set; } = "";
    public string Severity { get; set; } = "MEDIUM";
    public bool Accepted { get; set; }
    public string? AcceptedReason { get; set; }
    public bool Actionable { get; set; }
    /// <summary>mapped (css-map properties) | geometry (css-map roles) | match | style | state | layout | responsive (automatic inventory).</summary>
    public string Layer { get; set; } = CssLayers.Mapped;
    /// <summary>Fix instruction of the row (same text as the CSS report's Action column); read by the target-fixer.</summary>
    public string Action { get; set; } = "";
    /// <summary>"css-map" or "profile &lt;name&gt;" when accepted.</summary>
    public string? AcceptedBy { get; set; }
    /// <summary>Accessibility role of the element (automatic rows; used by profile acceptances).</summary>
    public string? Role { get; set; }
    /// <summary>The element is interactive on either side (severity rules: color HIGH above the deltaE threshold).</summary>
    public bool Interactive { get; set; }
    /// <summary>Rule that set the severity (CssSeverityRules), e.g. "deltaE 1.8 &lt; 3 -&gt; LOW"; null = default severity.</summary>
    public string? SeverityRule { get; set; }
    /// <summary>WCAG contrast ratio of the element's text color on its background (color rows of automatic pairs).</summary>
    public double? ContrastReference { get; set; }
    public double? ContrastTarget { get; set; }
    /// <summary>Page-level CSS group (G1..Gn) the row belongs to (CssGrouper).</summary>
    public string? GroupId { get; set; }
    /// <summary>Stable group key (10 hex) used by the fixer as css:group:&lt;key&gt;.</summary>
    public string? GroupKey { get; set; }
    /// <summary>Grid cell rows: column header text per column of the row, in column order ("column N" when no header was found).</summary>
    public List<string>? Columns { get; set; }
}

/// <summary>Values of <see cref="CssDiffEntry.Layer"/>.</summary>
public static class CssLayers
{
    public const string Mapped = "mapped";
    public const string Geometry = "geometry";
    public const string Match = "match";
    public const string Style = "style";
    public const string State = "state";
    public const string Layout = "layout";
    public const string Responsive = "responsive";
    public static readonly string[] All = { Mapped, Geometry, Match, Style, State, Layout, Responsive };
}

public sealed class UnmatchedItem
{
    public string Kind { get; set; } = "";
    public string Step { get; set; } = "";
    public string State { get; set; } = "";
    public string Field { get; set; } = "";
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
    public string Severity { get; set; } = "LOW";
}

/// <summary>Thrown when an input file exists but cannot be read or parsed.</summary>
public sealed class ReportInputException : Exception
{
    public ReportInputException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Reads and deserializes a JSON file. Missing file → null; unreadable/corrupt → ReportInputException.</summary>
    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            throw new ReportInputException($"Unreadable input {path}: {ex.Message}", ex);
        }
    }

    public static JsonNode? ReadNode(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (Exception ex)
        {
            throw new ReportInputException($"Unreadable input {path}: {ex.Message}", ex);
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
    }
}

// ═══════════════════════════════════════════════════════════════════════
// GENERATOR (design §5.1 classification, §6 CSS + reports)
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Browser-free generator for the four per-module reports, runs/Module&lt;X&gt;/summary.json and
/// (module "ALL") reports/FinalReport.md. All statuses default to FAIL and are upgraded only
/// from recorded data.
/// </summary>
public static class ModuleReportGenerator
{
    public const string KindCoverage = "test-coverage";
    public const string KindSelector = "test-selector";
    public const string KindExpectation = "test-expectation";
    public const string KindApp = "app";
    public const string KindUnmatchedAssertion = "unmatched-target-assertion";
    public const string KindUnmatchedCapture = "unmatched-target-capture";

    /// <summary>Entry point. module = "A".."Z" or "ALL" (every runs/Module* dir + reports/FinalReport.md via FinalReportBuilder).</summary>
    /// <param name="visualChecks">Geometry tolerances; null = the VisualChecks block of appsettings.test.json (defaults when absent).</param>
    public static IReadOnlyList<ModuleSummary> Generate(string pageDir, string module, VisualChecksSettings? visualChecks = null)
    {
        if (string.IsNullOrWhiteSpace(pageDir)) throw new ArgumentException("pageDir is required", nameof(pageDir));
        pageDir = Path.GetFullPath(pageDir);
        var vc = visualChecks ?? VisualChecksSettings.Load(HarnessPaths.Resolve("appsettings.test.json"));
        var plan = ReportJson.Read<PlanFile>(Path.Combine(pageDir, "state-map", "plan.json"));
        var cssMap = ReportJson.Read<CssMapFile>(Path.Combine(pageDir, "state-map", "css-map.json"));
        var status = ReportJson.ReadNode(Path.Combine(pageDir, "status.json"));

        var all = string.IsNullOrWhiteSpace(module) || module.Equals("ALL", StringComparison.OrdinalIgnoreCase);
        var modules = all ? DiscoverModules(pageDir, plan) : new List<string> { module.Trim().ToUpperInvariant() };

        var profile = StackProfile.Load(HarnessPaths.Resolve("profiles"), vc.StackProfile);

        var generated = new List<ModuleCss>();
        foreach (var m in modules)
            generated.Add(GenerateModule(pageDir, m, plan, cssMap, status, vc, profile));

        // Page-level pass: groups across every module of the page, then each module's CSS_Diff.md and summary.json.
        WritePageCss(pageDir, plan, cssMap, status, profile, vc, generated);

        var results = generated.Select(g => g.Summary).ToList();
        if (all) FinalReportBuilder.Write(pageDir, results, plan, status);
        return results;
    }

    /// <summary>Per-module inputs of the page-level CSS pass.</summary>
    private sealed class ModuleCss
    {
        public ModuleSummary Summary = new();
        public string PageName = "";
        public string RunDir = "";
        public List<string> PlannedStates = new();
    }

    /// <summary>Plan module ids first, then any other module, ordinal.</summary>
    private static List<string> ModuleOrder(PlanFile? plan, IEnumerable<string> modules)
    {
        var order = new List<string>();
        foreach (var pm in plan?.Modules ?? new List<PlanModule>())
            if (!order.Contains(pm.Id.ToUpperInvariant())) order.Add(pm.Id.ToUpperInvariant());
        foreach (var m in modules.Select(m => m.ToUpperInvariant()).OrderBy(m => m, StringComparer.Ordinal))
            if (!order.Contains(m)) order.Add(m);
        return order;
    }

    /// <summary>
    /// Groups the CSS rows of every module of the page (the modules just generated plus the other runs/Module*/summary.json
    /// files), so page-wide styling is one finding owned by the first module in plan order. Sets the group counts on each
    /// summary and writes runs/css_groups.json, runs/css_rows_all.json/.md, reports/CSS_Groups.md, then every module's
    /// Module&lt;X&gt;_CSS_Diff.md and summary.json (summary last: it is the completion marker).
    /// </summary>
    private static void WritePageCss(string pageDir, PlanFile? plan, CssMapFile? cssMap, JsonNode? status, StackProfile profile,
        VisualChecksSettings vc, List<ModuleCss> generated)
    {
        var mods = new List<ModuleCss>(generated);
        foreach (var x in DiscoverModules(pageDir, plan))
        {
            if (mods.Any(m => m.Summary.Module.Equals(x, StringComparison.OrdinalIgnoreCase))) continue;
            var runDir = Path.Combine(pageDir, "runs", $"Module{x}");
            ModuleSummary? s;
            try { s = ReportJson.Read<ModuleSummary>(Path.Combine(runDir, "summary.json")); }
            catch (ReportInputException ex)
            {
                Console.Error.WriteLine($"WARN: {ex.Message} (module left out of the page-level CSS groups)");
                continue;
            }
            if (s == null) continue; // module still running or never reported
            var pm = plan?.Modules.FirstOrDefault(m => m.Id.Equals(x, StringComparison.OrdinalIgnoreCase));
            mods.Add(new ModuleCss
            {
                Summary = s, RunDir = runDir,
                PageName = plan?.Page is { Length: > 0 } p ? p : Path.GetFileName(pageDir),
                PlannedStates = pm?.CssStates.ToList() ?? ReadStatusCssStates(status, x)
            });
        }
        var order = ModuleOrder(plan, mods.Select(m => m.Summary.Module));
        mods = mods.OrderBy(m => order.IndexOf(m.Summary.Module.ToUpperInvariant())).ToList();

        var rows = mods.SelectMany(m => m.Summary.CssDiffs.Select(d => (m.Summary.Module, d))).ToList();
        var issue = new IssueContext
        {
            AllStates = mods.SelectMany(m => m.PlannedStates).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
            ContrastMin = vc.Severity?.ContrastMin ?? 4.5
        };
        var result = CssGrouper.Build(rows, order, profile, vc.Groups, issue);
        foreach (var m in mods)
        {
            var s = m.Summary;
            s.ActionableCssRows = s.CssDiffs.Count(d => d.Actionable);
            s.CssGroupsSeen = result.Groups.Where(g => g.Modules.Contains(s.Module, StringComparer.OrdinalIgnoreCase)).Select(g => g.Id).ToList();
            s.CssGroupsOwned = result.Groups.Where(g => g.Owner.Equals(s.Module, StringComparison.OrdinalIgnoreCase)).Select(g => g.Id).ToList();
            s.ActionableCssDiffs = result.Groups.Count(g => g.Actionable && g.Owner.Equals(s.Module, StringComparison.OrdinalIgnoreCase));
        }

        var pageName = mods.FirstOrDefault()?.PageName ?? plan?.Page ?? Path.GetFileName(pageDir);
        var runs = Path.Combine(pageDir, "runs");
        ReportJson.Write(Path.Combine(runs, "css_groups.json"), new CssGroupsFile
        {
            Page = pageName, Profile = profile.Name, Modules = mods.Select(m => m.Summary.Module).ToList(),
            RawRows = result.Rows.Count, ActionableRows = result.Rows.Count(r => r.Actionable),
            ActionableGroups = result.Groups.Count(g => g.Actionable), Groups = result.Groups
        });
        ReportJson.Write(Path.Combine(runs, "css_rows_all.json"), result.Rows);
        WriteText(Path.Combine(runs, "css_rows_all.md"), BuildCssAudit(pageName, result));
        var reportsDir = Path.Combine(pageDir, "reports");
        WriteText(Path.Combine(reportsDir, "CSS_Groups.md"), BuildCssGroupsReport(pageName, profile.Name, result));

        foreach (var m in mods)
        {
            WriteText(Path.Combine(reportsDir, $"Module{m.Summary.Module}_CSS_Diff.md"),
                BuildCssReport(m.PageName, m.Summary.Module, m.RunDir, cssMap, m.Summary, m.PlannedStates, result.Groups));
            ReportJson.Write(Path.Combine(m.RunDir, "summary.json"), m.Summary);
        }
    }

    private static List<string> DiscoverModules(string pageDir, PlanFile? plan)
    {
        var runs = Path.Combine(pageDir, "runs");
        var found = Directory.Exists(runs)
            ? Directory.GetDirectories(runs, "Module*").Select(d => Path.GetFileName(d)!["Module".Length..])
                .Where(s => s.Length > 0).ToList()
            : new List<string>();
        // Keep plan order first, then anything else found on disk.
        var ordered = new List<string>();
        if (plan != null)
            foreach (var pm in plan.Modules)
                if (found.Contains(pm.Id, StringComparer.OrdinalIgnoreCase)) ordered.Add(pm.Id.ToUpperInvariant());
        foreach (var f in found.OrderBy(x => x, StringComparer.Ordinal))
            if (!ordered.Contains(f, StringComparer.OrdinalIgnoreCase)) ordered.Add(f);
        return ordered;
    }

    // ───────────────────────────────────────────────────────────────────
    // Per module
    // ───────────────────────────────────────────────────────────────────

    private static ModuleCss GenerateModule(string pageDir, string module, PlanFile? plan, CssMapFile? cssMap, JsonNode? status,
        VisualChecksSettings vc, StackProfile profile)
    {
        var runDir = Path.Combine(pageDir, "runs", $"Module{module}");
        var reference = ReportJson.Read<RunResultsFile>(Path.Combine(runDir, "Reference", "results.json"));
        var target = ReportJson.Read<RunResultsFile>(Path.Combine(runDir, "Target", "results.json"));
        var planModule = plan?.Modules.FirstOrDefault(m => m.Id.Equals(module, StringComparison.OrdinalIgnoreCase));
        var plannedStates = planModule?.CssStates.ToList() ?? ReadStatusCssStates(status, module);

        var summary = new ModuleSummary { Module = module };
        var cls = Classify(reference, target);
        summary.Findings = cls.Findings;
        summary.Unmatched = cls.Unmatched;
        // Content sanity checks: Target hits are Functional Gaps (kind app); Reference hits are Reference defects (not counted).
        var content = ClassifyContent(module, reference, target);
        summary.Findings.AddRange(content.TargetFindings);
        summary.ReferenceDefects = content.ReferenceDefects;
        summary.ReferenceDefectCount = content.ReferenceDefects.Count;
        summary.OpenFindings = summary.Findings.Count(f => f.Actionable);

        var css = CompareCss(runDir, cssMap, plannedStates, reference, target, vc);
        css.AddRange(CompareInventories(runDir, CssStates(plannedStates, reference, target), cssMap, profile, vc, css));
        CssSeverityRules.Apply(css, vc.Severity);
        ApplyAcceptance(css, cssMap, profile);
        foreach (var d in css) d.Action = ActionFor(d);
        summary.StackProfile = profile.Name;
        summary.CssDiffs = css;
        summary.CssDiffsTotal = css.Count;
        summary.ActionableCssRows = css.Count(d => d.Actionable);
        summary.ActionableCssDiffs = summary.ActionableCssRows; // replaced by the owned group count in WritePageCss

        var notTestedSteps = NotTestedSteps(reference, "Reference").Concat(NotTestedSteps(target, "Target")).ToList();
        var planNotTested = planModule?.NotTested ?? new List<PlanNotTested>();
        summary.NotTested = notTestedSteps.Count + planNotTested.Count;

        summary.ReferenceStatus = ReferencePasses(reference) ? "PASS" : "FAIL";
        summary.TargetStatus = TargetPasses(target, summary) ? "PASS" : "FAIL";
        summary.GeneratedAt = DateTimeOffset.Now.ToString("o");

        var reportsDir = Path.Combine(pageDir, "reports");
        Directory.CreateDirectory(reportsDir);
        var pageName = reference?.Page ?? target?.Page ?? plan?.Page ?? Path.GetFileName(pageDir);

        WriteText(Path.Combine(reportsDir, $"Module{module}_Parity.md"),
            BuildParity(pageName, module, reference, target, summary, new FieldLabels(plan?.FieldLabels)));
        WriteText(Path.Combine(reportsDir, $"Module{module}_Performance.md"),
            BuildPerformance(pageName, module, reference, target));
        WriteText(Path.Combine(reportsDir, $"Module{module}_ManualTests.md"),
            BuildManualTests(pageName, module, notTestedSteps, planNotTested));

        // Module<X>_CSS_Diff.md and summary.json (LAST: the runner treats it as the completion marker) are written by
        // the page-level CSS pass (WritePageCss), which needs the rows of every module.
        return new ModuleCss { Summary = summary, PageName = pageName, RunDir = runDir, PlannedStates = plannedStates };
    }

    private static List<string> ReadStatusCssStates(JsonNode? status, string module)
    {
        try
        {
            var arr = status?["modules"]?[module]?["cssStates"] as JsonArray;
            return arr?.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    internal static bool ReferencePasses(RunResultsFile? r) =>
        r != null && !r.Aborted && r.Steps.Count > 0 && r.Totals.StepsFail == 0 && r.Totals.AssertionsFailed == 0
        && r.Steps.All(s => s.Status is "PASS" or "NOT TESTED") && r.Steps.Any(s => s.Status == "PASS");

    // Raw actionable rows (not owned groups): a module whose rows belong to another module's group never passes.
    private static bool TargetPasses(RunResultsFile? t, ModuleSummary s) =>
        ReferencePasses(t) && s.OpenFindings == 0 && s.ActionableCssRows == 0;

    internal static List<(string System, RunStep Step)> NotTestedSteps(RunResultsFile? r, string system) =>
        r?.Steps.Where(s => s.Status == "NOT TESTED").Select(s => (system, s)).ToList() ?? new();

    // ───────────────────────────────────────────────────────────────────
    // Classification (design §5.1, ordered first match)
    // ───────────────────────────────────────────────────────────────────

    public sealed class ClassificationResult
    {
        public List<Finding> Findings { get; } = new();
        public List<UnmatchedItem> Unmatched { get; } = new();
    }

    private sealed class KeyData
    {
        public string Step = "";
        public string Field = "";
        public readonly List<(RunAssertion Ref, RunAssertion Tgt)> Paired = new();
        public readonly List<RunAssertion> RefOnly = new();
        public readonly List<RunAssertion> TargetAssertions = new();
        public readonly List<(string State, string RefVal, string TgtVal)> CaptureMismatch = new();
        public readonly List<(string State, string Field, string RefVal)> MissingCaptures = new();

        public IEnumerable<(RunAssertion Ref, RunAssertion Tgt)> ExpectationDiffs =>
            Paired.Where(p => p.Ref.ExpectedIsConstant && p.Tgt.ExpectedIsConstant
                && !string.Equals(p.Ref.Expected.Trim(), p.Tgt.Expected.Trim(), StringComparison.Ordinal));

        public IEnumerable<(RunAssertion Ref, RunAssertion Tgt)> ActualDiffs =>
            Paired.Where(p => p.Ref.Type is "CHANGES" or "STAYS SAME"
                && !string.Equals(p.Ref.Actual.Trim(), p.Tgt.Actual.Trim(), StringComparison.Ordinal));

        public IEnumerable<RunAssertion> FailedTarget => TargetAssertions.Where(a => !a.Passed);

        public bool HasCandidate => FailedTarget.Any() || ExpectationDiffs.Any() || ActualDiffs.Any()
            || CaptureMismatch.Count > 0 || RefOnly.Count > 0 || MissingCaptures.Count > 0;
    }

    private static bool IsPseudoStep(string step) => step is "NAV" or "PRE";

    public static ClassificationResult Classify(RunResultsFile? reference, RunResultsFile? target)
    {
        var result = new ClassificationResult();
        var refSteps = reference?.Steps ?? new List<RunStep>();
        var tgtSteps = target?.Steps ?? new List<RunStep>();
        var tgtStepNames = new HashSet<string>(tgtSteps.Select(s => s.Step), StringComparer.Ordinal) { "NAV", "PRE" };

        // Abort context: Target results missing/aborted, an ABORTED step, or a NAV FAIL → row 2 test-selector.
        var abortedStep = tgtSteps.FirstOrDefault(s => s.Status == "FAIL" && s.Description.StartsWith("ABORTED", StringComparison.Ordinal))?.Step;
        var navFailed = tgtSteps.Any(s => s.Step == "NAV" && s.Status == "FAIL");
        var targetAborted = target == null || target.Aborted || navFailed || abortedStep != null;
        var abortReason = target == null ? "no results.json"
            : abortedStep != null ? tgtSteps.First(s => s.Step == abortedStep).Description
            : navFailed ? tgtSteps.First(s => s.Step == "NAV").Description
            : target.AbortReason ?? "test aborted";

        var keys = new Dictionary<(string, string), KeyData>();
        KeyData Key(string step, string field)
        {
            if (!keys.TryGetValue((step, field), out var k))
                keys[(step, field)] = k = new KeyData { Step = step, Field = field };
            return k;
        }

        // Step order for stable output: Reference order, then Target-only steps.
        var stepOrder = new List<string> { "NAV" };
        foreach (var s in refSteps.Select(s => s.Step).Concat(tgtSteps.Select(s => s.Step)))
            if (!stepOrder.Contains(s)) stepOrder.Add(s);
        if (!stepOrder.Contains("PRE")) stepOrder.Add("PRE");

        // Assertions paired on (step, field, type) in order of occurrence.
        var refAssert = refSteps.SelectMany(s => s.Assertions.Select(a => (s.Step, a))).ToList();
        var tgtAssert = tgtSteps.SelectMany(s => s.Assertions.Select(a => (s.Step, a))).ToList();
        var tgtQueues = tgtAssert.GroupBy(x => (x.Step, x.a.Field, x.a.Type))
            .ToDictionary(g => g.Key, g => new Queue<RunAssertion>(g.Select(x => x.a)));
        var pairedTargets = new HashSet<RunAssertion>(ReferenceEqualityComparer.Instance);
        foreach (var (step, a) in refAssert)
        {
            var k = Key(step, a.Field);
            if (tgtQueues.TryGetValue((step, a.Field, a.Type), out var q) && q.Count > 0)
            {
                var t = q.Dequeue();
                pairedTargets.Add(t);
                k.Paired.Add((a, t));
            }
            else k.RefOnly.Add(a);
        }
        foreach (var (step, a) in tgtAssert)
        {
            Key(step, a.Field).TargetAssertions.Add(a);
            if (!pairedTargets.Contains(a))
                result.Unmatched.Add(new UnmatchedItem
                {
                    Kind = KindUnmatchedAssertion, Step = step, Field = a.Field, Type = a.Type,
                    Value = $"expected \"{a.Expected}\", actual \"{a.Actual}\" ({(a.Passed ? "passed" : "FAILED")})"
                });
        }

        // Captures paired on (state, field), last wins.
        var refCaps = LastCaptures(reference);
        var tgtCaps = LastCaptures(target);
        foreach (var ((state, field), rc) in refCaps)
        {
            if (tgtCaps.TryGetValue((state, field), out var tc))
            {
                if (!string.Equals(rc.Value.Trim(), tc.Value.Trim(), StringComparison.Ordinal))
                    Key(tc.Step, field).CaptureMismatch.Add((state, rc.Value, tc.Value));
            }
            else
            {
                // Missing capture keyed on the Reference capture's step (absorbed by row 1 if that step is missing).
                Key(rc.Step, field).MissingCaptures.Add((state, field, rc.Value));
            }
        }
        foreach (var ((state, field), tc) in tgtCaps)
            if (!refCaps.ContainsKey((state, field)))
                result.Unmatched.Add(new UnmatchedItem
                {
                    Kind = KindUnmatchedCapture, Step = tc.Step, State = state, Field = field, Value = tc.Value
                });

        // Row 1 / abort: Reference steps with no Target step (NAV/PRE are present on both).
        // A Reference NOT TESTED step has no baseline to mirror; it is listed in ManualTests instead.
        var missingSteps = refSteps.Where(s => s.Status != "NOT TESTED").Select(s => s.Step).Distinct()
            .Where(s => !tgtStepNames.Contains(s)).ToList();
        var absorbed = new HashSet<(string, string)>();

        if (targetAborted)
        {
            // One test-selector finding for the abort point, absorbing every unreached Reference step.
            var anchor = abortedStep ?? (navFailed ? "NAV" : missingSteps.FirstOrDefault() ?? "NAV");
            var f = NewFinding(target?.Module ?? reference?.Module ?? "", anchor, "*", KindSelector);
            f.Severity = "HIGH";
            f.Reference = "step executed";
            f.Target = abortReason;
            f.Sources.Add("step");
            f.Details.Add($"Target did not complete: {abortReason}");
            var absorbSteps = missingSteps.ToList();
            if (abortedStep != null) absorbSteps.Add(abortedStep);
            if (navFailed && !absorbSteps.Contains("NAV")) absorbSteps.Add("NAV");
            foreach (var s in absorbSteps.Distinct())
            {
                if (s != anchor) f.Details.Add($"Step {s} not reached on Target");
                foreach (var k in keys.Values.Where(k => k.Step == s))
                {
                    AttachMissing(f, k);
                    absorbed.Add((k.Step, k.Field));
                }
            }
            result.Findings.Add(f);
        }
        else
        {
            foreach (var s in missingSteps)
            {
                var refStep = refSteps.First(x => x.Step == s);
                var f = NewFinding(reference?.Module ?? "", s, "*", KindCoverage);
                f.Severity = "HIGH";
                f.Reference = Shorten(refStep.Title.Length > 0 ? refStep.Title : refStep.Description, 120);
                f.Target = "(step missing)";
                f.Sources.Add("step");
                f.Details.Add($"Reference step {s} has no Target step with the same name");
                foreach (var k in keys.Values.Where(k => k.Step == s))
                {
                    AttachMissing(f, k);
                    if (k.CaptureMismatch.Count > 0) f.Sources.Add("capture");
                    absorbed.Add((k.Step, k.Field));
                }
                f.Sources = f.Sources.Distinct().ToList();
                result.Findings.Add(f);
            }
        }

        // Rows 2–5 for every remaining key with at least one candidate.
        var tgtAllCaps = target?.Captures ?? new List<RunCapture>();
        var ordered = keys.Values
            .OrderBy(k => stepOrder.IndexOf(k.Step) < 0 ? int.MaxValue : stepOrder.IndexOf(k.Step))
            .ToList(); // OrderBy is stable → field order of first appearance within a step.
        foreach (var k in ordered)
        {
            if (absorbed.Contains((k.Step, k.Field)) || !k.HasCandidate) continue;
            var module = target?.Module ?? reference?.Module ?? "";

            var emptyCapture = tgtAllCaps.Any(c => c.Step == k.Step && c.Field == k.Field
                && (c.Value.Trim().Length == 0 || c.Value.Trim() == "__missing"));
            string kind;
            if (emptyCapture) kind = KindSelector;
            else if (k.ExpectationDiffs.Any()) kind = KindExpectation;
            else if (k.FailedTarget.Any() || k.ActualDiffs.Any() || k.CaptureMismatch.Count > 0) kind = KindApp;
            else kind = KindCoverage;

            var f = NewFinding(module, k.Step, k.Field, kind);
            FillValues(f, k, kind);
            AttachMissing(f, k);
            result.Findings.Add(f);
        }

        return result;
    }

    internal static Dictionary<(string State, string Field), RunCapture> LastCaptures(RunResultsFile? r)
    {
        var d = new Dictionary<(string, string), RunCapture>();
        if (r == null) return d;
        foreach (var c in r.Captures) d[(c.State, c.Field)] = c; // last wins
        return d;
    }

    private static Finding NewFinding(string module, string step, string field, string kind) => new()
    {
        Id = $"{module}-{step}-{field}",
        Kind = kind,
        Step = step,
        Field = field,
        Actionable = true
    };

    private static void AttachMissing(Finding f, KeyData k)
    {
        foreach (var a in k.RefOnly)
        {
            f.MissingAssertions.Add(new MissingAssertion { Type = a.Type, Expected = a.Expected });
            f.Details.Add($"Missing on Target: {a.Type} {k.Step}/{k.Field} (Reference expected \"{a.Expected}\", actual \"{a.Actual}\")");
        }
        foreach (var c in k.MissingCaptures)
        {
            f.MissingCaptures.Add(new MissingCapture { State = c.State, Field = c.Field });
            f.Details.Add($"Missing on Target: capture {c.State}/{c.Field} (Reference \"{c.RefVal}\")");
        }
        if (k.RefOnly.Count > 0 && !f.Sources.Contains("missing-assertion")) f.Sources.Add("missing-assertion");
        if (k.MissingCaptures.Count > 0 && !f.Sources.Contains("missing-capture")) f.Sources.Add("missing-capture");
    }

    /// <summary>Sets reference/target/expected/severity from the first differing value pair and records sources.</summary>
    private static void FillValues(Finding f, KeyData k, string kind)
    {
        var pairs = new List<(string Ref, string Tgt, string Expected)>();

        if (kind == KindExpectation)
            foreach (var p in k.ExpectationDiffs)
            {
                pairs.Add((p.Ref.Expected, p.Tgt.Expected, p.Ref.Expected));
                f.Details.Add($"{p.Ref.Type} expected constant differs: Reference \"{p.Ref.Expected}\" vs Target \"{p.Tgt.Expected}\"");
            }

        foreach (var t in k.FailedTarget)
        {
            var partner = k.Paired.FirstOrDefault(p => ReferenceEquals(p.Tgt, t));
            if (partner.Ref != null) pairs.Add((partner.Ref.Actual, t.Actual, partner.Ref.Expected));
            else pairs.Add((t.Expected, t.Actual, t.Expected));
            f.Details.Add($"Target {t.Type} failed: {t.Message}");
        }
        foreach (var p in k.ActualDiffs)
        {
            pairs.Add((p.Ref.Actual, p.Tgt.Actual, p.Ref.Actual));
            f.Details.Add($"{p.Ref.Type} actual differs: Reference \"{p.Ref.Before()}\" -> \"{p.Ref.Actual}\" vs Target \"{p.Tgt.Before()}\" -> \"{p.Tgt.Actual}\"");
        }
        foreach (var c in k.CaptureMismatch)
        {
            pairs.Add((c.RefVal, c.TgtVal, c.RefVal));
            f.Details.Add($"Captured {c.State}/{k.Field}: Reference \"{c.RefVal}\" vs Target \"{c.TgtVal}\"");
        }

        if (k.FailedTarget.Any() || k.ActualDiffs.Any() || k.ExpectationDiffs.Any()) f.Sources.Add("assertion");
        if (k.CaptureMismatch.Count > 0) f.Sources.Add("capture");

        if (kind == KindCoverage)
        {
            var a = k.RefOnly.FirstOrDefault();
            if (a != null) { f.Reference = a.Actual; f.Expected = a.Expected; f.Target = "(not asserted)"; }
            else
            {
                var c = k.MissingCaptures.First();
                f.Reference = c.RefVal; f.Expected = c.RefVal; f.Target = "(not captured)";
            }
            f.Severity = "MEDIUM";
            return;
        }

        var first = pairs.FirstOrDefault(p => !string.Equals(p.Ref.Trim(), p.Tgt.Trim(), StringComparison.Ordinal));
        if (first.Ref == null && pairs.Count > 0) first = pairs[0];
        if (first.Ref != null)
        {
            f.Reference = first.Ref;
            f.Target = first.Tgt;
            f.Expected = first.Expected;
            f.Severity = GapSeverity(first.Ref, first.Tgt) ?? "MEDIUM";
        }
        else f.Severity = "MEDIUM";
        if (kind == KindSelector) f.Details.Add("A Target capture on this key is empty or __missing (selector did not match)");
    }

    private static string Before(this RunAssertion a) => a.Expected; // CHANGES/STAYS SAME: expected = pre-state

    // ───────────────────────────────────────────────────────────────────
    // Content sanity checks (steps[].contentIssues)
    // ───────────────────────────────────────────────────────────────────

    public const string FieldLeakedSyntax = "page.leakedSyntax";
    public const string FieldConsoleError = "page.consoleError";
    public const string FieldPageError = "page.pageError";

    public sealed class ContentClassification
    {
        public List<Finding> TargetFindings { get; } = new();
        public List<ReferenceDefect> ReferenceDefects { get; } = new();
    }

    public const string FieldBrokenImage = "page.brokenImage";
    public const string FieldFailedRequest = "page.failedRequest";
    public const string FieldEmptySection = "page.emptySection";
    public const string FieldA11y = "page.a11y";

    internal static string ContentField(string kind) => kind switch
    {
        ContentIssue.LeakedSyntax => FieldLeakedSyntax,
        ContentIssue.PageError => FieldPageError,
        ContentIssue.BrokenImage => FieldBrokenImage,
        ContentIssue.FailedRequest => FieldFailedRequest,
        ContentIssue.EmptySection => FieldEmptySection,
        ContentIssue.Accessibility => FieldA11y,
        _ => FieldConsoleError
    };

    /// <summary>HIGH for leaked server/template syntax, broken images and failed requests; MEDIUM for console/page errors, empty sections and accessibility gaps.</summary>
    internal static string ContentSeverity(string kind) =>
        kind is ContentIssue.LeakedSyntax or ContentIssue.BrokenImage or ContentIssue.FailedRequest ? "HIGH" : "MEDIUM";

    private static string ContentExpected(string kind) => kind switch
    {
        ContentIssue.LeakedSyntax => "no server/template syntax in the visible page text",
        ContentIssue.PageError => "no unhandled page errors",
        ContentIssue.BrokenImage => "every visible image loads",
        ContentIssue.FailedRequest => "no failed network requests (HTTP 4xx/5xx or network errors)",
        ContentIssue.EmptySection => "every visible heading/section has content",
        ContentIssue.Accessibility => "every control, button and image has an accessible name",
        _ => "no browser console errors"
    };

    /// <summary>Content issues in step order (first step entry per name, as results.json stores them).</summary>
    private static List<(string Step, ContentIssue Issue)> ContentIssues(RunResultsFile? r) =>
        r?.Steps.SelectMany(s => (s.ContentIssues ?? new()).Select(i => (s.Step, i))).ToList() ?? new();

    /// <summary>
    /// Target content issues → one Functional Gap (kind app) per field (page.leakedSyntax HIGH, page.consoleError /
    /// page.pageError MEDIUM), id {X}-{firstStep}-{field}, excerpts deduped across steps. Reference content issues →
    /// one Reference defect per (kind, excerpt); they never count toward Target parity.
    /// </summary>
    public static ContentClassification ClassifyContent(string module, RunResultsFile? reference, RunResultsFile? target)
    {
        var result = new ContentClassification();
        var refIssues = ContentIssues(reference);
        var tgtIssues = ContentIssues(target);

        foreach (var kind in ContentIssue.AllKinds)
        {
            var hits = tgtIssues.Where(x => x.Issue.Kind == kind).ToList();
            if (hits.Count == 0) continue;
            var field = ContentField(kind);
            var f = NewFinding(module, hits[0].Step, field, KindApp);
            f.Severity = ContentSeverity(kind);
            f.Expected = ContentExpected(kind);
            var onRef = refIssues.FirstOrDefault(x => x.Issue.Kind == kind).Issue;
            f.Reference = onRef == null ? "(none on Reference)" : $"also on Reference (Reference defect): {onRef.Excerpt}";
            f.Target = hits[0].Issue.Excerpt;
            f.Sources.Add("content-scan");
            foreach (var g in hits.GroupBy(x => x.Issue.Excerpt, StringComparer.Ordinal))
            {
                var steps = string.Join(", ", g.Select(x => x.Step).Distinct());
                var tokens = g.First().Issue.Tokens;
                f.Details.Add($"{steps}: \"{g.Key}\"" + (tokens.Count > 0 ? $" (tokens: {string.Join(", ", tokens)})" : ""));
            }
            result.TargetFindings.Add(f);
        }

        var n = 0;
        foreach (var g in refIssues.GroupBy(x => (x.Issue.Kind, x.Issue.Excerpt)))
        {
            var first = g.First();
            result.ReferenceDefects.Add(new ReferenceDefect
            {
                Id = $"{module}-REF{++n}",
                Step = first.Step,
                Kind = first.Issue.Kind,
                Severity = ContentSeverity(first.Issue.Kind),
                Tokens = first.Issue.Tokens.ToList(),
                Excerpt = first.Issue.Excerpt,
                Steps = g.Select(x => x.Step).Distinct().ToList()
            });
        }
        return result;
    }

    // ───────────────────────────────────────────────────────────────────
    // Gap severity (design §6)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// LOW = only whitespace, case or ',' differ; MEDIUM = both sides have digits and the digit
    /// sequences match but the text differs; HIGH = digit sequences differ or any other
    /// difference. Returns null when the trimmed values are identical.
    /// </summary>
    public static string? GapSeverity(string? reference, string? target)
    {
        var r = (reference ?? "").Trim();
        var t = (target ?? "").Trim();
        if (string.Equals(r, t, StringComparison.Ordinal)) return null;
        static string Norm(string s) => Regex.Replace(s, @"[\s,]", "").ToLowerInvariant();
        if (Norm(r) == Norm(t)) return "LOW";
        var rd = Regex.Matches(r, @"\d+").Select(m => m.Value).ToList();
        var td = Regex.Matches(t, @"\d+").Select(m => m.Value).ToList();
        if (rd.Count > 0 && rd.SequenceEqual(td)) return "MEDIUM";
        return "HIGH";
    }

    // ───────────────────────────────────────────────────────────────────
    // CSS (design §6)
    // ───────────────────────────────────────────────────────────────────

    public static string DefaultCssSeverity(string property)
    {
        var p = property.Trim().ToLowerInvariant();
        if (p is "color" or "background-color" or "font-weight" or "display") return "HIGH";
        if (p is "font-family" or "text-align") return "LOW";
        if (p is "font-size" or "height" or "width" or "line-height" or "border-radius"
            || p.StartsWith("padding") || p.StartsWith("margin") || p.StartsWith("border")) return "MEDIUM";
        return "MEDIUM"; // unknown property: not downgraded (false PASS is never acceptable)
    }

    // ───────────────────────────────────────────────────────────────────
    // Geometry (layout.* keys in css_mapped_<state>.json)
    // ───────────────────────────────────────────────────────────────────

    public const string LayoutPrefix = CssCompareHelper.LayoutPrefix;

    /// <summary>CSS diff property of a role element whose geometry could not be measured (counted, MEDIUM).</summary>
    public const string UnmeasuredProperty = "(unmeasured)";

    /// <summary>
    /// null = within tolerance (|d| ≤ MinDiffPx or d% ≤ MinDiffPct, d% = |d| / max(|reference|, 1) × 100);
    /// otherwise HIGH when |d| &gt; HighDiffPx or d% &gt; HighDiffPct, else MEDIUM. Never LOW.
    /// </summary>
    public static string? GeometrySeverity(double reference, double target, GeometryTolerance? tol = null)
    {
        tol ??= new GeometryTolerance();
        var d = Math.Abs(target - reference);
        var pct = d / Math.Max(Math.Abs(reference), 1) * 100;
        if (!(d > tol.MinDiffPx && pct > tol.MinDiffPct)) return null;
        return d > tol.HighDiffPx || pct > tol.HighDiffPct ? "HIGH" : "MEDIUM";
    }

    internal static string Px(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + "px";

    private static bool TryPx(string? s, out double v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return double.TryParse(s.Trim().Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private static double? Layout(Dictionary<string, string> props, string key) =>
        props.TryGetValue(LayoutPrefix + key, out var s) && TryPx(s, out var v) ? v : null;

    /// <summary>Top of the pager's visible content (layout.contentTop), else its box top (older captures).</summary>
    private static double? PagerTop(Dictionary<string, string> props) => Layout(props, "contentTop") ?? Layout(props, "y");

    /// <summary>One geometry row when Reference and Target are both known and outside tolerance; null otherwise.</summary>
    private static CssDiffEntry? GeometryRow(CssMapElement el, string state, string prop, double? r, double? t, GeometryTolerance tol)
    {
        if (r == null || t == null) return null;
        var sev = GeometrySeverity(r.Value, t.Value, tol);
        if (sev == null) return null;
        string? reason = null;
        var accepted = el.Accepted?.TryGetValue(prop, out reason) == true;
        return new CssDiffEntry
        {
            Element = el.Name, State = state, Property = prop, Reference = Px(r.Value), Target = Px(t.Value),
            Severity = sev, Accepted = accepted, AcceptedReason = accepted ? reason : null,
            AcceptedBy = accepted ? AcceptedByCssMap : null, Layer = CssLayers.Geometry, Actionable = !accepted
        };
    }

    /// <summary>
    /// Derived layout metrics per state from the first css-map element with each role:
    /// grid → layout.height; row → layout.rowHeight (first visible row); header → layout.headerHeight;
    /// pager → layout.gapToPager = top of the pager's visible content (layout.contentTop, else the box top) −
    /// bottom of the last visible row (needs a row), so empty space inside a tall pager container still counts;
    /// filter → layout.gapToGrid = grid top − filter bottom (needs a grid).
    /// A role element the metric cannot be measured on never passes silently (false PASS is never acceptable):
    /// Reference selector miss → counted "(unmeasured)" row (the CSS loop only lists it in the notes);
    /// Target miss → the CSS loop's "(element)" row; pager/filter without the row/grid role in the state →
    /// counted "(unmeasured: needs role ...)" row.
    /// </summary>
    internal static List<CssDiffEntry> CompareGeometry(string state, Dictionary<string, Dictionary<string, string>> refMapped,
        Dictionary<string, Dictionary<string, string>> tgtMapped, CssMapFile cssMap, GeometryTolerance tol)
    {
        var rows = new List<CssDiffEntry>();
        CssMapElement? Find(string role) => cssMap.Elements.FirstOrDefault(e => e.State == state
            && string.Equals(e.Role?.Trim(), role, StringComparison.OrdinalIgnoreCase));
        (CssMapElement El, Dictionary<string, string> R, Dictionary<string, string> T)? Role(string role)
        {
            var el = Find(role);
            if (el == null) return null;
            var refOk = refMapped.TryGetValue(el.Name, out var r) && !IsMissing(r!);
            var tgtOk = tgtMapped.TryGetValue(el.Name, out var t) && !IsMissing(t!);
            if (!refOk)
                rows.Add(new CssDiffEntry
                {
                    Element = el.Name, State = state, Property = UnmeasuredProperty, Reference = "missing",
                    Target = tgtOk ? "present" : "missing", Severity = "MEDIUM", Actionable = true
                });
            return refOk && tgtOk ? (el, r!, t!) : null;
        }
        void Add(CssDiffEntry? e) { if (e != null) rows.Add(e); }
        void NeedsRole(string role, string needed)
        {
            var el = Find(role);
            if (el == null || Find(needed) != null) return;
            rows.Add(new CssDiffEntry
            {
                Element = el.Name, State = state, Property = $"{UnmeasuredProperty[..^1]}: needs role {needed})",
                Reference = "-", Target = "-", Severity = "MEDIUM", Actionable = true
            });
        }

        var grid = Role("grid");
        var row = Role("row");
        var header = Role("header");
        var pager = Role("pager");
        var filter = Role("filter");
        NeedsRole("pager", "row");
        NeedsRole("filter", "grid");

        if (grid is { } g && g.El.Properties?.ContainsKey(LayoutPrefix + "height") != true) // listed raw key: compared by the CSS loop
            Add(GeometryRow(g.El, state, LayoutPrefix + "height", Layout(g.R, "height"), Layout(g.T, "height"), tol));
        if (row is { } rw)
            Add(GeometryRow(rw.El, state, LayoutPrefix + "rowHeight", Layout(rw.R, "height"), Layout(rw.T, "height"), tol));
        if (header is { } h)
            Add(GeometryRow(h.El, state, LayoutPrefix + "headerHeight", Layout(h.R, "height"), Layout(h.T, "height"), tol));
        if (pager is { } p && row is { } pr)
            Add(GeometryRow(p.El, state, LayoutPrefix + "gapToPager",
                PagerTop(p.R) - Layout(pr.R, "lastBottom"), PagerTop(p.T) - Layout(pr.T, "lastBottom"), tol));
        if (filter is { } f && grid is { } fg)
            Add(GeometryRow(f.El, state, LayoutPrefix + "gapToGrid",
                Layout(fg.R, "y") - (Layout(f.R, "y") + Layout(f.R, "height")),
                Layout(fg.T, "y") - (Layout(f.T, "y") + Layout(f.T, "height")), tol));
        foreach (var r in rows) r.Layer = CssLayers.Geometry;
        return rows;
    }

    public const string AcceptedByCssMap = "css-map";
    public const string StylePrefix = CssCompareHelper.StylePrefix;

    /// <summary>Every state compared for a module: planned states first, then any state captured on either side.</summary>
    private static List<string> CssStates(List<string> plannedStates, RunResultsFile? reference, RunResultsFile? target)
    {
        var states = new List<string>(plannedStates);
        foreach (var s in (reference?.CssStatesCaptured ?? new()).Concat(target?.CssStatesCaptured ?? new()))
            if (!states.Contains(s)) states.Add(s);
        return states;
    }

    private static List<CssDiffEntry> CompareCss(string runDir, CssMapFile? cssMap, List<string> plannedStates,
        RunResultsFile? reference, RunResultsFile? target, VisualChecksSettings? vc = null)
    {
        vc ??= new VisualChecksSettings();
        var tol = vc.Geometry ?? new GeometryTolerance();
        var ignore = vc.IgnoreProperties ?? Array.Empty<string>();
        var diffs = new List<CssDiffEntry>();
        var mapMissing = cssMap == null || (reference?.CssMapMissing ?? false) || (target?.CssMapMissing ?? false);
        var states = CssStates(plannedStates, reference, target);

        var tgtCaptured = new HashSet<string>(target?.CssStatesCaptured ?? new List<string>());
        foreach (var state in states)
        {
            var refMapped = ReadMapped(Path.Combine(runDir, "Reference", "css", $"css_mapped_{state}.json"));
            var tgtMapped = ReadMapped(Path.Combine(runDir, "Target", "css", $"css_mapped_{state}.json"));
            var tgtHasState = tgtCaptured.Contains(state) || tgtMapped != null
                || File.Exists(Path.Combine(runDir, "Target", "css", $"css_full_{state}.json"));

            if (plannedStates.Contains(state) && !tgtHasState)
            {
                diffs.Add(new CssDiffEntry
                {
                    Element = "(state)", State = state, Property = "(not captured on Target)",
                    Reference = refMapped != null ? "captured" : "-", Target = "missing",
                    Severity = "MEDIUM", Actionable = true
                });
                continue;
            }
            if (mapMissing)
            {
                if (plannedStates.Contains(state))
                    diffs.Add(new CssDiffEntry
                    {
                        Element = "(unmapped)", State = state, Property = "(css-map.json missing)",
                        Reference = "-", Target = "-", Severity = "MEDIUM", Actionable = true
                    });
                continue;
            }
            if (refMapped == null || tgtMapped == null) continue;

            foreach (var el in cssMap!.Elements.Where(e => e.State == state))
            {
                if (!refMapped.TryGetValue(el.Name, out var rProps) || IsMissing(rProps)) continue; // Reference selector problem: listed in notes
                if (!tgtMapped.TryGetValue(el.Name, out var tProps) || IsMissing(tProps))
                {
                    diffs.Add(new CssDiffEntry
                    {
                        Element = el.Name, State = state, Property = "(element)", Reference = "present",
                        Target = "missing", Severity = "MEDIUM", Actionable = true
                    });
                    continue;
                }
                var interactive = rProps.GetValueOrDefault(StylePrefix + "interactive") == "true"
                    || tProps.GetValueOrDefault(StylePrefix + "interactive") == "true";
                foreach (var (prop, rRaw) in rProps)
                {
                    if (prop.StartsWith("__", StringComparison.Ordinal) || prop.StartsWith(StylePrefix, StringComparison.Ordinal)) continue;
                    if (prop.StartsWith(LayoutPrefix, StringComparison.Ordinal))
                    {
                        // Raw geometry keys are compared only when the element lists them in properties (opt-in).
                        if (el.Properties?.ContainsKey(prop) == true && tProps.TryGetValue(prop, out var tRaw)
                            && TryPx(rRaw, out var rv) && TryPx(tRaw, out var tv))
                        {
                            var row = GeometryRow(el, state, prop, rv, tv, tol);
                            if (row != null) diffs.Add(row);
                        }
                        continue;
                    }
                    // Ignore list (no visual effect): only on non-interactive elements.
                    if (!interactive && ignore.Any(i => StackProfile.PropertyMatches(i, prop))) continue;
                    if (!tProps.TryGetValue(prop, out var tVal)) tVal = "";
                    var rVal = rRaw;
                    // Normalized inputs when both captures have them: the background seen through transparent
                    // ancestors, and the first font family that actually renders (not the whole fallback stack).
                    if (prop == "background-color" && rProps.TryGetValue(StylePrefix + "effectiveBackground", out var rb)
                        && tProps.TryGetValue(StylePrefix + "effectiveBackground", out var tb)) { rVal = rb; tVal = tb; }
                    if (prop == "font-family" && rProps.TryGetValue(StylePrefix + "renderedFont", out var rf)
                        && tProps.TryGetValue(StylePrefix + "renderedFont", out var tf)) { rVal = rf; tVal = tf; }
                    if (string.Equals(rVal?.Trim(), tVal?.Trim(), StringComparison.Ordinal)) continue;
                    if (MigrationTests.Playwright.CssCompareHelper.IsNoiseDiff(prop, rVal ?? "", tVal ?? "")) continue;
                    string? mapSev = null, reason = null;
                    var sev = el.Properties?.TryGetValue(prop, out mapSev) == true && !string.IsNullOrWhiteSpace(mapSev)
                        ? mapSev.Trim().ToUpperInvariant() : DefaultCssSeverity(prop);
                    var accepted = el.Accepted?.TryGetValue(prop, out reason) == true;
                    diffs.Add(new CssDiffEntry
                    {
                        Element = el.Name, State = state, Property = prop, Reference = rVal ?? "", Target = tVal ?? "",
                        Severity = sev, Accepted = accepted, AcceptedReason = accepted ? reason : null,
                        AcceptedBy = accepted ? AcceptedByCssMap : null, Layer = CssLayers.Mapped,
                        Actionable = !accepted && (sev == "HIGH" || sev == "MEDIUM"), Interactive = interactive,
                        // A severity written in css-map.json is the page author's decision: CssSeverityRules keeps it.
                        SeverityRule = !string.IsNullOrWhiteSpace(mapSev) ? $"css-map.json severity {sev}" : null
                    });
                }
            }
            diffs.AddRange(CompareGeometry(state, refMapped, tgtMapped, cssMap, tol));
        }
        return diffs;
    }

    // ───────────────────────────────────────────────────────────────────
    // Automatic inventory comparison (inventory_<state>.json on both sides)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// For every state with an inventory on both sides: AutoCssComparer rows, plus runs/Module&lt;X&gt;/match_&lt;state&gt;.json
    /// (pairs with score/how, missing, extra) for the audit. Also gives the mapped/geometry rows of css-map elements
    /// the accessibility role of their inventoried element (profile acceptances can filter on it).
    /// </summary>
    internal static List<CssDiffEntry> CompareInventories(string runDir, List<string> states, CssMapFile? cssMap,
        StackProfile profile, VisualChecksSettings vc, List<CssDiffEntry> existing)
    {
        var rows = new List<CssDiffEntry>();
        foreach (var state in states)
        {
            var refInv = ReportJson.Read<InventoryFile>(Path.Combine(runDir, "Reference", "css", $"inventory_{state}.json"));
            var tgtInv = ReportJson.Read<InventoryFile>(Path.Combine(runDir, "Target", "css", $"inventory_{state}.json"));
            if (refInv == null || tgtInv == null) continue;
            var result = AutoCssComparer.Compare(state, refInv, tgtInv, profile, vc);
            rows.AddRange(result.Rows);

            var roles = refInv.Elements.Where(e => !string.IsNullOrEmpty(e.MapName))
                .GroupBy(e => e.MapName!).ToDictionary(g => g.Key, g => g.First().Role);
            foreach (var d in existing.Where(d => d.State == state && d.Role == null && roles.ContainsKey(d.Element)))
                d.Role = roles[d.Element];

            object El(InventoryElement e) => new { e.Index, e.Role, e.Name, e.MapName, e.DomPath, e.Box };
            ReportJson.Write(Path.Combine(runDir, $"match_{state}.json"), new
            {
                State = state,
                Profile = profile.Name,
                MinMatchScore = vc.Inventory?.MinMatchScore ?? 0.6,
                Counts = new
                {
                    Reference = refInv.Elements.Count, Target = tgtInv.Elements.Count, Pairs = result.Match.Pairs.Count,
                    MapPairs = result.Match.Pairs.Count(p => p.How == "map"),
                    MissingOnTarget = result.Match.MissingOnTarget.Count, ExtraOnTarget = result.Match.ExtraOnTarget.Count,
                    ExcludedLayoutCells = result.Match.ExcludedLayoutCells.Count,
                    LayoutTextCells = result.Match.LayoutTextCells.Count,
                    Rows = result.Rows.Count
                },
                CaptureMs = new { Reference = refInv.CaptureMs, Target = tgtInv.CaptureMs },
                Pairs = result.Match.Pairs.Select(p => new { p.How, p.Score, Reference = El(p.Reference), Target = El(p.Target) }),
                MissingOnTarget = result.Match.MissingOnTarget.Select(El),
                ExtraOnTarget = result.Match.ExtraOnTarget.Select(El),
                // Layout-table cells (no header cells, nested tables): not content, left out of matching (ElementMatcher.IsLayoutCell).
                ExcludedLayoutCells = result.Match.ExcludedLayoutCells.Select(x => new { x.Side, x.Element.Index, x.Element.Role, x.Element.Name, x.Element.DomPath, x.Element.Box }),
                // Layout cells with their own text: compared by text (found on the other side or a LOW row).
                LayoutTextCells = result.Match.LayoutTextCells.Select(x => new { x.Side, x.Element.Index, x.Text, x.Found, x.Element.DomPath, x.Element.Box })
            });
        }
        return rows;
    }

    /// <summary>
    /// Acceptance order: profile acceptances apply first, css-map.json `accepted` (explicit, per page) overrides them.
    /// Mapped/geometry rows already carry their css-map acceptance; automatic rows of a css-map element (label = its
    /// name) take it here. Accepted rows are never actionable; other automatic rows are actionable when HIGH/MEDIUM.
    /// </summary>
    internal static void ApplyAcceptance(List<CssDiffEntry> rows, CssMapFile? cssMap, StackProfile profile)
    {
        foreach (var d in rows)
        {
            var auto = d.Layer is not (CssLayers.Mapped or CssLayers.Geometry);
            if (auto && !d.Accepted && cssMap != null)
            {
                var el = cssMap.Elements.FirstOrDefault(e => e.State == d.State && e.Name == d.Element);
                if (el?.Accepted != null && el.Accepted.TryGetValue(d.Property, out var reason))
                {
                    d.Accepted = true;
                    d.AcceptedReason = reason;
                    d.AcceptedBy = AcceptedByCssMap;
                }
            }
            if (!d.Accepted && profile.FindAcceptance(d, d.Role) is { } pa)
            {
                d.Accepted = true;
                d.AcceptedReason = pa.Reason;
                d.AcceptedBy = $"profile {profile.Name}";
            }
            if (d.Accepted) d.Actionable = false;
            // Reference-worse responsive rows (only the Reference overflows/clips; the Target fits): listed, not counted.
            else if (IsReferenceWorse(d)) d.Actionable = false;
            else if (auto) d.Actionable = d.Severity is "HIGH" or "MEDIUM";
        }
    }

    /// <summary>
    /// A responsive row where only the Reference shows the problem (it overflows, scrolls or is clipped at a width where
    /// the Target fits): an old-page problem, never a Target fix. Listed under "Reference worse (not counted)".
    /// </summary>
    internal static bool IsReferenceWorse(CssDiffEntry d) =>
        !d.Accepted && d.Layer == CssLayers.Responsive && OnlyReferenceHasProblem(d);

    /// <summary>"accepted by profile &lt;name&gt;: &lt;reason&gt;" for profile acceptances, the css-map reason otherwise.</summary>
    internal static string AcceptedNote(CssDiffEntry d) =>
        d.AcceptedBy != null && d.AcceptedBy.StartsWith("profile ", StringComparison.Ordinal)
            ? $"accepted by {d.AcceptedBy}: {d.AcceptedReason}"
            : d.AcceptedReason ?? "";

    private static string StatePseudo(string property)
    {
        var dot = property.IndexOf('.');
        var prefix = dot > 0 ? property[..dot] : "";
        return prefix switch { "hover" => ":hover", "focus" => ":focus", "disabled" => ":disabled", _ => "" };
    }

    /// <summary>
    /// Fix instruction of a CSS diff row (the CSS report's Action column; summary.json cssDiffs[].action for the fixer).
    /// Px layout/responsive rows where the Reference is larger never ask the Target for extra space (GeometryAction).
    /// </summary>
    internal static string ActionFor(CssDiffEntry d)
    {
        if (d.Property == AutoCssComparer.MissingProperty)
            return $"Render {d.Element} on Target with the same role and accessible name as the Reference (or pair it in state-map/css-map.json if it exists under another name)";
        if (d.Property == AutoCssComparer.ExtraProperty)
            return "Confirm the extra Target element is intended or remove it (never add it to the Reference)";
        if (d.Property == AutoCssComparer.LayoutTextMissingProperty)
            return $"Check that the Target shows the text '{d.Reference}' (layout-table text on the Reference, not found in a compared Target element; not counted)";
        if (d.Property == AutoCssComparer.LayoutTextExtraProperty)
            return $"Confirm the Target text '{d.Target}' is intended (layout-table text on the Target, not found in a compared Reference element; not counted)";
        if (d.Property.StartsWith("("))
            return d.Element == "(unmapped)" ? "Add state-map/css-map.json entries for this state" :
                d.Property == UnmeasuredProperty ? "Geometry not measured: the Reference selector matched nothing; fix the Reference selector of this element in state-map/css-map.json (never the Reference source)" :
                d.Property.StartsWith(UnmeasuredProperty[..^1], StringComparison.Ordinal) ? $"Geometry not measured: add a state-map/css-map.json element with role '{d.Property[(d.Property.LastIndexOf(' ') + 1)..^1]}' in this state" :
                d.Property == "(element)" ? "Make the mapped element render on Target (or fix the Target selector in css-map.json)" :
                "Capture this planned state on Target (CssCompareHelper.DiscoverAndCaptureAll)";
        if (d.Property.StartsWith("text.", StringComparison.Ordinal))
            return $"Align to reference: {d.Property["text.".Length..]} text '{d.Reference}'";
        if (d.Property.StartsWith(LayoutPrefix, StringComparison.Ordinal))
            return GeometryAction(d);
        if (d.Property.StartsWith("responsive.", StringComparison.Ordinal))
        {
            var parts = d.Property.Split('.');
            var width = parts.Length > 1 ? parts[1] : "";
            return TryPx(d.Reference, out _) && TryPx(d.Target, out _)
                ? GeometryAction(d)
                : OnlyReferenceHasProblem(d) ? ReferenceOnlyAction(d)
                : $"Fix Target layout at {width}px width: {d.Target} vs Reference {d.Reference}";
        }
        var pseudo = StatePseudo(d.Property);
        if (pseudo.Length > 0)
            return $"Align to reference: set {StyleNormalizer.BaseProperty(d.Property)}: {d.Reference} on {pseudo}";
        return $"Align to reference: set {d.Property}: {d.Reference}";
    }

    private static bool IsMissing(Dictionary<string, string> props) =>
        props.TryGetValue("__missing", out var v) && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, Dictionary<string, string>>? ReadMapped(string path)
    {
        var node = ReportJson.ReadNode(path) as JsonObject;
        if (node == null) return null;
        var d = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (name, val) in node)
        {
            var props = new Dictionary<string, string>();
            if (val is JsonObject o)
                foreach (var (p, v) in o)
                    props[p] = v is JsonValue jv ? (jv.TryGetValue<string>(out var s) ? s : jv.ToJsonString()) : v?.ToJsonString() ?? "";
            d[name] = props;
        }
        return d;
    }

    // ───────────────────────────────────────────────────────────────────
    // Markdown reports
    // ───────────────────────────────────────────────────────────────────

    internal static string Cell(string? s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");

    internal static string Shorten(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";

    internal static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    internal static string StepStatus(RunStep s) => s.Status is "PASS" or "NOT TESTED" ? s.Status : "FAIL";

    /// <summary>Step table; descriptions in plain English (P7) with the raw field names kept as "(field: ...)" for the audit.</summary>
    internal static void AppendStepReport(StringBuilder sb, RunResultsFile? r, string system, FieldLabels? labels = null)
    {
        labels ??= new FieldLabels();
        sb.AppendLine("| Step | Status | Description |");
        sb.AppendLine("|------|--------|-------------|");
        if (r == null)
        {
            sb.AppendLine($"| - | FAIL | {system} results.json missing: the test did not produce results |");
            return;
        }
        foreach (var s in r.Steps)
        {
            var st = StepStatus(s);
            sb.AppendLine($"| {Cell(s.Step)} | {st} | {Cell(labels.Describe(s, st, system == "Target", withFieldNames: true))} |");
        }
        if (r.Steps.Count == 0) sb.AppendLine("| - | FAIL | No steps recorded |");
        if (r.Aborted) sb.AppendLine($"| (abort) | FAIL | Test aborted: {Cell(r.AbortReason)} |");
    }

    internal static string BuildParity(string page, string module, RunResultsFile? reference, RunResultsFile? target, ModuleSummary s, FieldLabels? labels = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Module {module} Parity Report - {page}");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"Reference: **{s.ReferenceStatus}** ({Totals(reference)})  ");
        // Raw rows here: this report is written before the page-level CSS pass sets the owned group count (see CSS_Diff.md for groups).
        sb.AppendLine($"Target: **{s.TargetStatus}** ({Totals(target)}), open findings {s.OpenFindings}, actionable CSS rows {s.CssDiffs.Count(d => d.Actionable)} (page-level CSS groups: Module{module}_CSS_Diff.md)");
        sb.AppendLine();
        sb.AppendLine("How to read: Reference = the old page (baseline), Target = the new page. The same test steps run on both; the new page passes when every check matches and no functional gap or styling/layout issue is open.");
        sb.AppendLine();

        // ── Reference: 3 sections ──
        sb.AppendLine("## Reference baseline");
        sb.AppendLine();
        sb.AppendLine("### 1. Step Report");
        sb.AppendLine();
        sb.AppendLine("What the test did on the old page (Reference), step by step, with the recorded values (field names in brackets for the audit).");
        sb.AppendLine();
        AppendStepReport(sb, reference, "Reference", labels);
        sb.AppendLine();
        sb.AppendLine("### 2. HTML Diff Report");
        sb.AppendLine();
        sb.AppendLine("Grid changes seen on the old page that no check covered (each one may need a check in the tests).");
        sb.AppendLine();
        AppendHtmlDiffs(sb, reference);
        sb.AppendLine();
        sb.AppendLine("### 3. Summary");
        sb.AppendLine();
        sb.AppendLine($"- Status: {s.ReferenceStatus}");
        sb.AppendLine($"- {Totals(reference)}");
        sb.AppendLine($"- Baseline values captured: {reference?.Captures.Count ?? 0}");
        sb.AppendLine($"- Reference defects: {s.ReferenceDefectCount} (baseline issues found by the content checks; listed below, not counted in Target parity)");
        if (reference != null && reference.Captures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Values recorded on the old page that the new page must reproduce:");
            sb.AppendLine();
            sb.AppendLine("| Step | State | Field | Value |");
            sb.AppendLine("|------|-------|-------|-------|");
            foreach (var c in reference.Captures)
                sb.AppendLine($"| {Cell(c.Step)} | {Cell(c.State)} | {Cell(c.Field)} | {Cell(c.Value)} |");
        }
        sb.AppendLine();

        // ── Reference defects (content sanity checks on the baseline) ──
        sb.AppendLine("## Reference defects (baseline issues, not counted in Target parity)");
        sb.AppendLine();
        AppendReferenceDefects(sb, s);
        sb.AppendLine();

        // ── Target: 5 sections ──
        sb.AppendLine("## Target");
        sb.AppendLine();
        sb.AppendLine("### 1. Step Report");
        sb.AppendLine();
        sb.AppendLine("The same steps on the new page (Target); a FAIL row starts with what differed from the old page.");
        sb.AppendLine();
        AppendStepReport(sb, target, "Target", labels);
        sb.AppendLine();
        sb.AppendLine("### 2. Performance Comparison");
        sb.AppendLine();
        sb.AppendLine($"Time per step in milliseconds (ms), old page vs new page; informational only. Full table: [Module{module}_Performance.md](Module{module}_Performance.md)");
        sb.AppendLine();
        sb.AppendLine("| Step | Old page (Reference) ms | New page (Target) ms |");
        sb.AppendLine("|------|-------------------------|----------------------|");
        foreach (var name in StepNames(reference, target))
            sb.AppendLine($"| {Cell(name)} | {Ms(reference, name)} | {Ms(target, name)} |");
        sb.AppendLine($"| **Total** | {SumMs(reference)} | {SumMs(target)} |");
        sb.AppendLine();
        sb.AppendLine("### 3. Functional Gaps");
        sb.AppendLine();
        if (s.Findings.Count == 0) sb.AppendLine("0 open findings (every Reference assertion and capture is mirrored and matches).");
        else
        {
            labels ??= new FieldLabels();
            sb.AppendLine("Behavior or data that differs between the old page (Reference) and the new page (Target). What = the field in plain English (raw field in Field); Kind app = new page code, test-* = test code.");
            sb.AppendLine();
            sb.AppendLine("| ID | What | Kind | Severity | Step | Field | Reference | Target | Missing on Target | Evidence |");
            sb.AppendLine("|----|------|------|----------|------|-------|-----------|--------|-------------------|----------|");
            foreach (var f in s.Findings)
            {
                var missing = string.Join("; ", f.MissingAssertions.Select(m => $"{m.Type} \"{m.Expected}\"")
                    .Concat(f.MissingCaptures.Select(m => $"capture {m.State}/{m.Field}")));
                sb.AppendLine($"| {Cell(f.Id)} | {Cell(labels.Label(f.Field))} | {f.Kind} | {f.Severity} | {Cell(f.Step)} | {Cell(f.Field)} | {Cell(PlainValue(f.Field, f.Reference))} | {Cell(PlainValue(f.Field, f.Target))} | {Cell(missing)} | {Cell(string.Join("; ", f.Details.Take(4)))} |");
            }
        }
        if (s.Unmatched.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Unmatched Target items (LOW, not counted):");
            sb.AppendLine();
            sb.AppendLine("| Kind | Step | State | Field | Type | Value |");
            sb.AppendLine("|------|------|-------|-------|------|-------|");
            foreach (var u in s.Unmatched)
                sb.AppendLine($"| {u.Kind} | {Cell(u.Step)} | {Cell(u.State)} | {Cell(u.Field)} | {Cell(u.Type)} | {Cell(u.Value)} |");
        }
        sb.AppendLine();
        sb.AppendLine("### 4. CSS Diff Report");
        sb.AppendLine();
        sb.AppendLine($"Full table: [Module{module}_CSS_Diff.md](Module{module}_CSS_Diff.md)");
        sb.AppendLine();
        sb.AppendLine($"- Total CSS diffs: {s.CssDiffsTotal}");
        sb.AppendLine($"- Actionable raw rows (HIGH/MEDIUM, not accepted): {s.CssDiffs.Count(d => d.Actionable)}");
        if (s.CssGroupsSeen.Count > 0)
            sb.AppendLine($"- Page-level CSS groups: {s.ActionableCssDiffs} actionable owned by Module {module}, {s.CssGroupsSeen.Count} seen in this module (reports/CSS_Groups.md)");
        else
            sb.AppendLine("- Page-level CSS groups: reports/CSS_Groups.md");
        foreach (var g in s.CssDiffs.GroupBy(d => d.Severity).OrderBy(g => SevRank(g.Key)))
            sb.AppendLine($"- {g.Key}: {g.Count()} ({g.Count(d => d.Accepted)} accepted)");
        sb.AppendLine();
        sb.AppendLine("### 5. Summary");
        sb.AppendLine();
        sb.AppendLine($"- Target status: {s.TargetStatus}");
        sb.AppendLine($"- {Totals(target)}");
        sb.AppendLine($"- Open findings: {s.OpenFindings} ({string.Join(", ", s.Findings.GroupBy(f => f.Kind).Select(g => $"{g.Key} {g.Count()}"))})");
        sb.AppendLine($"- Actionable CSS rows: {s.CssDiffs.Count(d => d.Actionable)} of {s.CssDiffsTotal}" + (s.CssGroupsSeen.Count > 0 ? $"; actionable CSS groups owned: {s.ActionableCssDiffs}" : ""));
        sb.AppendLine($"- NOT TESTED items: {s.NotTested}");
        sb.AppendLine($"- Performance: Reference {SumMs(reference)} ms vs Target {SumMs(target)} ms (sum of stepMs)");
        sb.AppendLine();

        // ── Side by side ──
        sb.AppendLine("## Baseline vs Target values");
        sb.AppendLine();
        sb.AppendLine("Every captured value side by side (last capture per state and field); Match NO means the new page shows something else.");
        sb.AppendLine();
        sb.AppendLine("| State | Field | Reference | Target | Match |");
        sb.AppendLine("|-------|-------|-----------|--------|-------|");
        var refCaps = LastCaptures(reference);
        var tgtCaps = LastCaptures(target);
        var allKeys = refCaps.Keys.Concat(tgtCaps.Keys.Where(k => !refCaps.ContainsKey(k))).ToList();
        foreach (var key in allKeys)
        {
            var rv = refCaps.TryGetValue(key, out var rc) ? rc.Value : null;
            var tv = tgtCaps.TryGetValue(key, out var tc) ? tc.Value : null;
            var match = rv != null && tv != null && rv.Trim() == tv.Trim() ? "YES" : "NO";
            sb.AppendLine($"| {Cell(key.State)} | {Cell(key.Field)} | {Cell(rv ?? "(not captured)")} | {Cell(tv ?? "(not captured)")} | {match} |");
        }
        if (allKeys.Count == 0) sb.AppendLine("| - | - | (no captures) | (no captures) | NO |");
        return sb.ToString();
    }

    /// <summary>Audit value cell: the plain-English value with the raw value in brackets when they differ ("ascending (asc)").</summary>
    internal static string PlainValue(string? field, string? value)
    {
        var plain = FieldLabels.ValueText(field, value);
        return plain == (value ?? "") ? plain : $"{plain} ({value})";
    }

    /// <summary>Plain-English kind of a content issue ("Leaked server/template syntax", ...).</summary>
    internal static string ContentKindText(string kind) => kind switch
    {
        ContentIssue.LeakedSyntax => "Leaked server/template syntax",
        ContentIssue.ConsoleError => "Browser console error",
        ContentIssue.PageError => "Unhandled page error",
        ContentIssue.BrokenImage => "Broken image",
        ContentIssue.FailedRequest => "Failed network request",
        ContentIssue.EmptySection => "Empty section",
        ContentIssue.Accessibility => "Accessibility gap",
        _ => kind
    };

    /// <summary>Visible text of a Reference defect: the excerpt plus the matched tokens.</summary>
    internal static string ReferenceDefectText(ReferenceDefect r) =>
        r.Tokens.Count > 0 ? $"{r.Excerpt} (tokens: {string.Join(", ", r.Tokens)})" : r.Excerpt;

    /// <summary>Plain sentence before the raw excerpt, for views without a Kind column ("Server code is shown as text on the old page: ...").</summary>
    internal static string ReferenceDefectPlain(ReferenceDefect r)
    {
        var lead = r.Kind switch
        {
            ContentIssue.LeakedSyntax => "Server or template code is shown as page text on the old page:",
            ContentIssue.ConsoleError => "The old page logs a browser console error:",
            ContentIssue.PageError => "The old page throws an unhandled script error:",
            ContentIssue.BrokenImage => "An image does not load on the old page:",
            ContentIssue.FailedRequest => "A network request fails on the old page:",
            ContentIssue.EmptySection => "A heading or section on the old page has no content:",
            ContentIssue.Accessibility => "An element on the old page has no accessible name or text for screen readers:",
            _ => ""
        };
        // Excerpt shortened at a word boundary so the tokens always stay visible in a capped chat cell.
        var excerpt = r.Excerpt ?? "";
        if (excerpt.Length > 90)
        {
            var sp = excerpt.LastIndexOf(' ', 90);
            excerpt = (sp > 45 ? excerpt[..sp] : excerpt[..90]) + " ...";
        }
        var raw = r.Tokens.Count > 0 ? $"{excerpt} (tokens: {string.Join(", ", r.Tokens)})" : excerpt;
        return lead.Length == 0 ? raw : $"{lead} {raw}";
    }

    internal static void AppendReferenceDefects(StringBuilder sb, ModuleSummary s)
    {
        if (s.ReferenceDefects.Count == 0) { sb.AppendLine("0 Reference defects."); return; }
        sb.AppendLine("| ID | Severity | Step | Kind | Text |");
        sb.AppendLine("|----|----------|------|------|------|");
        foreach (var r in s.ReferenceDefects)
            sb.AppendLine($"| {Cell(r.Id)} | {r.Severity} | {Cell(string.Join(", ", r.Steps.DefaultIfEmpty(r.Step)))} | {Cell(ContentKindText(r.Kind))} | {Cell(ReferenceDefectText(r))} |");
    }

    internal static void AppendHtmlDiffs(StringBuilder sb, RunResultsFile? r)
    {
        if (r == null) { sb.AppendLine("FAIL: no results.json, HTML diffs unavailable."); return; }
        if (r.UncoveredHtmlDiffs.Count == 0) { sb.AppendLine("0 uncovered HTML diffs (every grid change was covered by an assertion)."); return; }
        sb.AppendLine("| Step | Uncovered change |");
        sb.AppendLine("|------|------------------|");
        foreach (var d in r.UncoveredHtmlDiffs)
        {
            var i = d.IndexOf(": ", StringComparison.Ordinal);
            sb.AppendLine(i > 0 ? $"| {Cell(d[..i])} | `{Cell(d[(i + 2)..])}` |" : $"| - | `{Cell(d)}` |");
        }
    }

    internal static string Totals(RunResultsFile? r) => r == null
        ? "results.json missing"
        : $"steps {r.Totals.StepsPass} PASS / {r.Totals.StepsFail} FAIL / {r.Totals.StepsNotTested} NOT TESTED; assertions {r.Totals.AssertionsPassed}/{r.Totals.AssertionsTotal} passed, {r.Totals.AssertionsFailed} failed"
          + (r.Aborted ? $"; ABORTED: {r.AbortReason}" : "");

    internal static int SevRank(string s) => s switch { "HIGH" => 0, "MEDIUM" => 1, "LOW" => 2, _ => 3 };

    internal static List<string> StepNames(RunResultsFile? r, RunResultsFile? t)
    {
        var names = new List<string>();
        foreach (var s in (r?.Steps ?? new()).Concat(t?.Steps ?? new()))
            if (!names.Contains(s.Step)) names.Add(s.Step);
        return names;
    }

    internal static RunStep? FindStep(RunResultsFile? r, string step) => r?.Steps.FirstOrDefault(s => s.Step == step);
    internal static string Ms(RunResultsFile? r, string step) => FindStep(r, step)?.StepMs.ToString() ?? "-";
    internal static long SumMs(RunResultsFile? r) => r?.Steps.Sum(s => s.StepMs) ?? 0;

    /// <summary>"mapped 3 (2 actionable), layout 4 (4 actionable)"; layers without rows are left out; "none" when empty.</summary>
    internal static string LayerCounts(IReadOnlyCollection<CssDiffEntry> rows)
    {
        var parts = CssLayers.All.Select(l => (l, rows.Where(d => (string.IsNullOrEmpty(d.Layer) ? CssLayers.Mapped : d.Layer) == l).ToList()))
            .Where(x => x.Item2.Count > 0).Select(x => $"{x.l} {x.Item2.Count} ({x.Item2.Count(d => d.Actionable)} actionable)").ToList();
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    /// <summary>"46: button 'Save', button 'Add', button 'Delete', ..." (count, then the example labels).</summary>
    internal static string GroupElements(CssGroup g) =>
        $"{g.ElementCount}: {string.Join(", ", g.Examples)}{(g.ElementCount > g.Examples.Count ? ", ..." : "")}";

    /// <summary>"A, C [page_load, S_p2]"; "A, B [all 8 states]" when the group shows in every planned state; more than 4 states: first 3 + "and N more".</summary>
    internal static string GroupSeenIn(CssGroup g) =>
        string.Join(", ", g.Modules) + (g.States.Count > 0 ? $" [{StatesText(g)}]" : "");

    /// <summary>"page_load, sorted" / "all 8 states" / "page_load, sorted, filtered and 5 more" (full list: css_groups.json).</summary>
    internal static string StatesText(CssGroup g)
    {
        if (g.InAllStates && g.States.Count > 1) return $"all {g.States.Count} states";
        if (g.States.Count > 4) return $"{string.Join(", ", g.States.Take(3))} and {g.States.Count - 3} more";
        return string.Join(", ", g.States);
    }

    /// <summary>Action of a group in module X's report: the group action for the owner, a pointer to the owner otherwise.</summary>
    internal static string GroupActionFor(CssGroup g, string module) =>
        g.Owner.Equals(module, StringComparison.OrdinalIgnoreCase) ? g.Action : $"see page-level group {g.Id} (owned by Module {g.Owner})";

    /// <summary>Issue cell: the plain-English sentence (older groups without one: the plain property name).</summary>
    internal static string IssueOf(CssGroup g) => string.IsNullOrEmpty(g.Issue) ? FieldLabels.PropertyText(g.Property) : g.Issue;

    private static void AppendThemeTable(StringBuilder sb, IReadOnlyList<CssGroup> groups, Func<CssGroup, string> action, string empty)
    {
        sb.AppendLine("| Issue | Group | Elements | Property | Reference | Target | Severity | Seen in | Action |");
        sb.AppendLine("|-------|-------|----------|----------|-----------|--------|----------|---------|--------|");
        if (groups.Count == 0) sb.AppendLine($"| {empty} | - | - | - | - | - | - | - | - |");
        foreach (var g in groups)
            sb.AppendLine($"| {Cell(IssueOf(g))} | {g.Id} | {Cell(GroupElements(g))} | {Cell(g.Property)} | {Cell(g.Reference)} | {Cell(g.Target)} | {g.Severity} | {Cell(GroupSeenIn(g))} | {Cell(action(g))} |");
    }

    /// <summary>"Grid pager [page_load, S_p2]": the element and the states it was seen in.</summary>
    private static string IndividualElement(CssGroup g) =>
        $"{string.Join(", ", g.Elements)} [{StatesText(g)}]";

    /// <summary>Element-specific findings in the CSS Diff Report format of reporting-standards.md (Issue | Element | Property | Reference | Target | Severity | Action).</summary>
    private static void AppendIndividualTable(StringBuilder sb, IReadOnlyList<CssGroup> groups, Func<CssGroup, string> action, string empty)
    {
        sb.AppendLine("| Issue | Element | Property | Reference | Target | Severity | Action |");
        sb.AppendLine("|-------|---------|----------|-----------|--------|----------|--------|");
        if (groups.Count == 0) sb.AppendLine($"| {empty} | - | - | - | - | - | - |");
        foreach (var g in groups)
            sb.AppendLine($"| {Cell(IssueOf(g))} | {Cell(IndividualElement(g))} | {Cell(g.Property)} | {Cell(g.Reference)} | {Cell(g.Target)} | {g.Severity} | {Cell(action(g))} |");
    }

    private static void AppendAcceptedGroups(StringBuilder sb, IEnumerable<CssGroup> groups)
    {
        var acc = groups.Where(g => g.Accepted).ToList();
        if (acc.Count == 0) sb.AppendLine("None.");
        foreach (var g in acc)
            sb.AppendLine($"- {IssueOf(g)} {AcceptedText(g)}" +
                          " Technical detail: " +
                          (g.Kind == CssGroup.Theme
                              ? $"{g.Id} {GroupElements(g)} [{StatesText(g)}] {g.Property}"
                              : $"{IndividualElement(g)} {g.Property}") +
                          $", Reference `{g.Reference}` vs Target `{g.Target}`.");
    }

    /// <summary>"Accepted by profile X: reason." / "Accepted by css-map.json: reason." (one sentence, ends with a period).</summary>
    private static string AcceptedText(CssGroup g)
    {
        var note = (g.AcceptedNote ?? "").Trim();
        var text = g.AcceptedBy?.StartsWith("profile ", StringComparison.Ordinal) == true && note.StartsWith("accepted by ", StringComparison.Ordinal)
            ? "A" + note[1..]
            : $"Accepted by css-map.json: {note}";
        return text.EndsWith('.') ? text : text + ".";
    }

    /// <summary>"Reference worse (not counted)": responsive rows where only the old page overflows/clips.</summary>
    private static void AppendReferenceWorse(StringBuilder sb, IReadOnlyList<CssGroup> groups, Func<CssGroup, string> action)
    {
        sb.AppendLine("Only the old page (Reference) has these problems; the new page (Target) fits. They are listed for the record and never counted or fixed in the new page.");
        sb.AppendLine();
        sb.AppendLine("| Issue | Element | Property | Reference | Target | Severity | Action |");
        sb.AppendLine("|-------|---------|----------|-----------|--------|----------|--------|");
        if (groups.Count == 0) sb.AppendLine("| None. | - | - | - | - | - | - |");
        foreach (var g in groups)
            sb.AppendLine($"| {Cell(IssueOf(g))} | {Cell(IndividualElement(g))} | {Cell(g.Property)} | {Cell(g.Reference)} | {Cell(g.Target)} | {g.Severity} | {Cell(action(g))} |");
    }

    /// <summary>Open groups (theme first, then individual) in the shared sort order: actionable, severity, element count desc. Reference-worse groups are listed on their own.</summary>
    internal static IEnumerable<CssGroup> OpenGroups(IEnumerable<CssGroup> groups) => groups.Where(g => !g.Accepted && !g.ReferenceWorse);

    /// <summary>Layout-table cell counts summed over the module's match_&lt;state&gt;.json files; null when none has them.</summary>
    internal static (int Excluded, int WithText, int NotFound)? LayoutCellCounts(string runDir, IEnumerable<string> states)
    {
        int excluded = 0, withText = 0, notFound = 0;
        var any = false;
        foreach (var state in states.Distinct(StringComparer.Ordinal))
        {
            var path = Path.Combine(runDir, $"match_{state}.json");
            if (!File.Exists(path)) continue;
            JsonNode? node;
            try { node = JsonNode.Parse(File.ReadAllText(path)); }
            catch (System.Text.Json.JsonException) { continue; }
            var counts = node?["counts"];
            if (counts?["excludedLayoutCells"] is not { } ex) continue;
            any = true;
            excluded += ex.GetValue<int>();
            withText += counts["layoutTextCells"]?.GetValue<int>() ?? 0;
            notFound += (node!["layoutTextCells"] as JsonArray)?.Count(x => x?["found"]?.GetValue<bool>() == false) ?? 0;
        }
        return any ? (excluded, withText, notFound) : null;
    }

    internal static string BuildCssReport(string page, string module, string runDir, CssMapFile? cssMap, ModuleSummary s, List<string> plannedStates,
        IReadOnlyList<CssGroup>? pageGroups = null)
    {
        // Without page groups (direct callers), group this module's rows on their own.
        pageGroups ??= CssGrouper.Build(s.CssDiffs.Select(d => (s.Module, d)).ToList(), new[] { s.Module }, null, null,
            new IssueContext { AllStates = plannedStates }).Groups;
        var mine = pageGroups.Where(g => g.Modules.Contains(module, StringComparer.OrdinalIgnoreCase)).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"# Module {module} CSS Diff Report - {page}");
        sb.AppendLine();
        sb.AppendLine($"Planned CSS states: {(plannedStates.Count == 0 ? "(none)" : string.Join(", ", plannedStates))}  ");
        sb.AppendLine($"css-map.json: {(cssMap == null ? "MISSING (every planned state is an unmapped diff)" : $"{cssMap.Elements.Count} mapped elements")}  ");
        sb.AppendLine($"Actionable CSS groups owned by Module {module}: {s.ActionableCssDiffs} (page-level groups seen in this module: {mine.Count}, {mine.Count(g => g.Actionable)} actionable; all groups: `reports/CSS_Groups.md`)  ");
        sb.AppendLine($"Raw rows: {s.CssDiffsTotal} total, {s.CssDiffs.Count(d => d.Actionable)} actionable (informational; every row with its group: `runs/css_rows_all.md`)  ");
        sb.AppendLine($"By layer: {LayerCounts(s.CssDiffs)}  ");
        if (LayoutCellCounts(runDir, plannedStates) is { } lc)
            sb.AppendLine($"Layout-table cells (tables that only position other elements): {lc.Excluded} left out of element matching (empty or holding only compared controls), " +
                          $"{lc.WithText} compared by their own text ({lc.NotFound} not found on the other side: LOW, not counted); per state in `runs/Module{module}/match_<state>.json`  ");
        sb.AppendLine($"Stack profile: {(string.IsNullOrEmpty(s.StackProfile) ? StackProfile.GenericName : s.StackProfile)}, {s.CssDiffs.Count(d => d.AcceptedBy?.StartsWith("profile ", StringComparison.Ordinal) == true)} rows accepted by profile");
        sb.AppendLine();
        sb.AppendLine(CssReadingGuide);
        sb.AppendLine();
        sb.AppendLine("## Theme groups");
        sb.AppendLine();
        sb.AppendLine(ThemePurpose);
        sb.AppendLine();
        AppendThemeTable(sb, OpenGroups(mine).Where(g => g.Kind == CssGroup.Theme).ToList(), g => GroupActionFor(g, module), "0 open CSS differences");
        sb.AppendLine();
        sb.AppendLine("## Individual findings");
        sb.AppendLine();
        sb.AppendLine(IndividualPurpose);
        sb.AppendLine();
        AppendIndividualTable(sb, OpenGroups(mine).Where(g => g.Kind != CssGroup.Theme).ToList(), g => GroupActionFor(g, module), "0 open CSS differences");
        sb.AppendLine();
        sb.AppendLine("## Reference worse (not counted)");
        sb.AppendLine();
        AppendReferenceWorse(sb, mine.Where(g => g.ReferenceWorse && !g.Accepted).ToList(), g => GroupActionFor(g, module));
        sb.AppendLine();
        sb.AppendLine("## Accepted differences");
        sb.AppendLine();
        sb.AppendLine(AcceptedPurpose);
        sb.AppendLine();
        AppendAcceptedGroups(sb, mine);
        sb.AppendLine();
        sb.AppendLine("## Capture files");
        sb.AppendLine();
        var any = false;
        foreach (var sys in new[] { "Reference", "Target" })
        {
            var dir = Path.Combine(runDir, sys, "css");
            if (!Directory.Exists(dir)) { sb.AppendLine($"- {sys}: no css/ folder"); continue; }
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                sb.AppendLine($"- {sys}: `runs/Module{module}/{sys}/css/{Path.GetFileName(f)}`");
                any = true;
            }
        }
        if (!any) sb.AppendLine("- (no capture files found)");
        if (cssMap != null)
        {
            var refMissing = new List<string>();
            foreach (var state in plannedStates)
            {
                var rm = ReadMapped(Path.Combine(runDir, "Reference", "css", $"css_mapped_{state}.json"));
                if (rm == null) continue;
                refMissing.AddRange(rm.Where(kv => IsMissing(kv.Value)).Select(kv => $"{kv.Key} [{state}]"));
            }
            if (refMissing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Notes");
                sb.AppendLine();
                sb.AppendLine($"Reference selector matched nothing (fix css-map.json, not compared): {string.Join(", ", refMissing)}");
            }
        }
        return sb.ToString();
    }

    /// <summary>reports/CSS_Groups.md: every page-level group (theme, individual, accepted) once, with its owner.</summary>
    internal static string BuildCssGroupsReport(string page, string profileName, CssGroupResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# CSS Groups - {page}");
        sb.AppendLine();
        sb.AppendLine("Page-level CSS findings: rows with the same role class, property and normalized Reference/Target values are one theme group " +
                      "(one CSS rule / theme token in the Target); element-specific rows (missing/extra, layout, responsive, geometry) stay individual, " +
                      "deduplicated across states and modules. Each group is counted once, in the first module of the plan it was seen in (Seen in).  ");
        sb.AppendLine($"Actionable groups: {r.Groups.Count(g => g.Actionable)} of {r.Groups.Count} " +
                      $"(theme {r.Groups.Count(g => g.Actionable && g.Kind == CssGroup.Theme)}, individual {r.Groups.Count(g => g.Actionable && g.Kind != CssGroup.Theme)}); " +
                      $"accepted groups: {r.Groups.Count(g => g.Accepted)}; Reference worse (not counted): {r.Groups.Count(g => g.ReferenceWorse && !g.Accepted)}  ");
        sb.AppendLine($"Raw rows: {r.Rows.Count} total, {r.Rows.Count(x => x.Actionable)} actionable (audit: `runs/css_rows_all.md`, `runs/css_rows_all.json`)  ");
        sb.AppendLine($"Stack profile: {profileName}");
        sb.AppendLine();
        sb.AppendLine(CssReadingGuide);
        sb.AppendLine();
        sb.AppendLine("## Theme groups");
        sb.AppendLine();
        sb.AppendLine(ThemePurpose);
        sb.AppendLine();
        AppendThemeTable(sb, OpenGroups(r.Groups).Where(g => g.Kind == CssGroup.Theme).ToList(), g => $"Module {g.Owner}: {g.Action}", "0 open CSS differences");
        sb.AppendLine();
        sb.AppendLine("## Individual findings");
        sb.AppendLine();
        sb.AppendLine(IndividualPurpose);
        sb.AppendLine();
        AppendIndividualTable(sb, OpenGroups(r.Groups).Where(g => g.Kind != CssGroup.Theme).ToList(), g => $"Module {g.Owner}: {g.Action}", "0 open CSS differences");
        sb.AppendLine();
        sb.AppendLine("## Reference worse (not counted)");
        sb.AppendLine();
        AppendReferenceWorse(sb, r.Groups.Where(g => g.ReferenceWorse && !g.Accepted).ToList(), g => $"Module {g.Owner}: {g.Action}");
        sb.AppendLine();
        sb.AppendLine("## Accepted groups");
        sb.AppendLine();
        sb.AppendLine(AcceptedPurpose);
        sb.AppendLine();
        AppendAcceptedGroups(sb, r.Groups);
        return sb.ToString();
    }

    /// <summary>How to read the CSS tables (one line above them in every CSS report).</summary>
    internal const string CssReadingGuide =
        "How to read: \"old page (Reference)\" is the original app, \"new page (Target)\" the migrated app. The Issue column says in plain English what differs; " +
        "the other columns keep the technical detail (element, CSS property, Reference and Target values, severity, where it was seen, and the fix).";

    internal const string ThemePurpose =
        "Styling differences shared by several elements of one kind: each row is usually one CSS rule or theme setting to change in the new page.";

    internal const string IndividualPurpose =
        "Differences of one specific element (missing or extra elements, text, layout, narrow windows, sizes): fix each one in the new page.";

    internal const string AcceptedPurpose =
        "Differences that were reviewed and accepted (by the page's css-map.json or the stack profile): listed for the record, not counted.";

    /// <summary>runs/css_rows_all.md: every raw row with its group id and the rule that set its severity.</summary>
    internal static string BuildCssAudit(string page, CssGroupResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# CSS rows audit - {page}");
        sb.AppendLine();
        sb.AppendLine($"Every raw CSS row of every module ({r.Rows.Count} rows, {r.Rows.Count(x => x.Actionable)} actionable) with its page-level group (reports/CSS_Groups.md).");
        sb.AppendLine();
        sb.AppendLine("| Module | Element | State | Property | Reference | Target | Severity | Severity rule | Layer | Accepted | Actionable | Group |");
        sb.AppendLine("|--------|---------|-------|----------|-----------|--------|----------|---------------|-------|----------|------------|-------|");
        foreach (var x in r.Rows)
            sb.AppendLine($"| {x.Module} | {Cell(x.Element)} | {Cell(x.State)} | {Cell(x.Property)} | {Cell(x.Reference)} | {Cell(x.Target)} | {x.Severity} | " +
                          $"{Cell(x.SeverityRule ?? "default")} | {x.Layer} | {(x.Accepted ? "yes" : "no")} | {(x.Actionable ? "yes" : "no")} | {x.GroupId} |");
        return sb.ToString();
    }

    /// <summary>
    /// Target larger: "Fix Target layout: layout.gapToPager 300px vs Reference 2px (diff +298px, 14900%)".
    /// Reference larger: the Reference may own the defect (e.g. empty space on the baseline), so the row tells
    /// the fixer to confirm instead of copying the extra space/height into the Target. Still counted either way.
    /// </summary>
    internal static string GeometryAction(CssDiffEntry d)
    {
        if (!TryPx(d.Reference, out var r) || !TryPx(d.Target, out var t))
            return OnlyReferenceHasProblem(d) ? ReferenceOnlyAction(d) : $"Fix Target layout: {d.Property} {d.Target} vs Reference {d.Reference}";
        var diff = t - r;
        var pct = Math.Abs(diff) / Math.Max(Math.Abs(r), 1) * 100;
        var delta = $"(diff {(diff >= 0 ? "+" : "-")}{Px(Math.Abs(diff))}, {pct.ToString("0", CultureInfo.InvariantCulture)}%)";
        if (r > t)
            return $"Reference larger: {d.Property} Reference {d.Reference} vs Target {d.Target} {delta}. " +
                   "Confirm on the Reference page: if the extra space/height is a Reference defect, accept it in css-map.json `accepted` " +
                   "(user decision); never add empty space or height to the Target to match";
        return $"Fix Target layout: {d.Property} {d.Target} vs Reference {d.Reference} {delta}";
    }

    /// <summary>
    /// Overflow / clipping / page-scroll rows where the Target is clean ("fits", "fully visible") and only the
    /// Reference shows the problem: the Target must not reproduce it. Same "Reference larger" contract as px rows.
    /// </summary>
    private static bool OnlyReferenceHasProblem(CssDiffEntry d) =>
        (d.Property.EndsWith(".overflow", StringComparison.Ordinal) || d.Property.EndsWith(".hidden", StringComparison.Ordinal)) &&
        IsCleanLayoutValue(d.Target) && !IsCleanLayoutValue(d.Reference);

    private static bool IsCleanLayoutValue(string? v) =>
        v != null && (v == "fits" || v.StartsWith("fits ", StringComparison.Ordinal) || v == "fully visible" || v == "visible");

    private static string ReferenceOnlyAction(CssDiffEntry d) =>
        $"Reference larger: {d.Property} Reference {d.Reference} vs Target {d.Target} (only the Reference shows it). " +
        "Confirm on the Reference page: if it is a Reference defect, accept it in css-map.json `accepted` " +
        "(user decision); never change the Target to reproduce it";

    /// <summary>
    /// Counted CSS groups owned by the module whose fix is "Reference larger" (the old page has the extra space/height):
    /// they stay counted, but the reader should confirm/accept them on the old page instead of changing the new page.
    /// </summary>
    internal static int ReferenceLargerGroups(ModuleSummary s) =>
        s.CssDiffs.Where(c => c.Actionable && !string.IsNullOrEmpty(c.GroupId) && s.CssGroupsOwned.Contains(c.GroupId, StringComparer.Ordinal)
                              && c.Action.StartsWith("Reference larger", StringComparison.Ordinal))
            .Select(c => c.GroupId).Distinct(StringComparer.Ordinal).Count();

    internal static string BuildPerformance(string page, string module, RunResultsFile? reference, RunResultsFile? target)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Module {module} Performance Comparison - {page}");
        sb.AppendLine();
        sb.AppendLine("Time per step on the old page (Reference) and the new page (Target), in milliseconds (ms). Informational only: speed never fails a module.");
        sb.AppendLine();
        sb.AppendLine("Times are stepMs (whole step: action, settle waits, assertions and HTML diff). Settle waits written into the tests are included on both sides. Delta = Target minus Reference (ms), Ratio = Target / Reference. SLOWER = Target > 1.5x Reference and > +500 ms (informational).");
        sb.AppendLine();
        sb.AppendLine("| Step | Action | Old page (Reference) ms | New page (Target) ms | Delta ms | Ratio |");
        sb.AppendLine("|------|--------|-------------------------|----------------------|----------|-------|");
        foreach (var name in StepNames(reference, target))
        {
            var r = FindStep(reference, name);
            var t = FindStep(target, name);
            var title = (r?.Title is { Length: > 0 } ? r.Title : t?.Title is { Length: > 0 } ? t.Title : Shorten(r?.Description ?? t?.Description, 60));
            var delta = r != null && t != null ? (t.StepMs - r.StepMs).ToString("+#;-#;0") : "-";
            var ratio = r != null && t != null && r.StepMs > 0 ? $"{(double)t.StepMs / r.StepMs:0.00}x" : "-";
            var slower = r != null && t != null && t.StepMs > 1.5 * r.StepMs && t.StepMs - r.StepMs > 500 ? " SLOWER" : "";
            sb.AppendLine($"| {Cell(name)} | {Cell(title)} | {r?.StepMs.ToString() ?? "-"} | {t?.StepMs.ToString() ?? "-"} | {delta} | {ratio}{slower} |");
        }
        var rs = SumMs(reference); var ts = SumMs(target);
        sb.AppendLine($"| **Total** | | {rs} | {ts} | {(reference != null && target != null ? (ts - rs).ToString("+#;-#;0") : "-")} | {(rs > 0 && target != null ? $"{(double)ts / rs:0.00}x" : "-")} |");
        sb.AppendLine();
        sb.AppendLine("Action-only times (actionMs): the click or typing itself, in milliseconds, without settle waits and checks.");
        sb.AppendLine();
        sb.AppendLine("| Step | Old page (Reference) action ms | New page (Target) action ms |");
        sb.AppendLine("|------|--------------------------------|-----------------------------|");
        foreach (var name in StepNames(reference, target))
            sb.AppendLine($"| {Cell(name)} | {FindStep(reference, name)?.ActionMs.ToString() ?? "-"} | {FindStep(target, name)?.ActionMs.ToString() ?? "-"} |");
        return sb.ToString();
    }

    internal static string BuildManualTests(string page, string module, List<(string System, RunStep Step)> steps, List<PlanNotTested> planItems)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Module {module} Manual Tests (NOT TESTED) - {page}");
        sb.AppendLine();
        var total = steps.Count + planItems.Count;
        if (total == 0) { sb.AppendLine("0 NOT_TESTED items."); return sb.ToString(); }
        sb.AppendLine($"{total} NOT_TESTED items.");
        sb.AppendLine();
        sb.AppendLine("Items the automation could not check; a person verifies each one by hand on both pages. Source: the plan, or the step (old page = Reference, new page = Target) that skipped it.");
        sb.AppendLine();
        sb.AppendLine("| Source | Item | Reason |");
        sb.AppendLine("|--------|------|--------|");
        foreach (var (sys, s) in steps)
        {
            var parts = s.Description.Split(" | ", 2);
            sb.AppendLine($"| {sys} {Cell(s.Step)} | {Cell(parts[0])} | {Cell(parts.Length > 1 ? parts[1] : "")} |");
        }
        foreach (var p in planItems)
            sb.AppendLine($"| plan.json | {Cell(p.Item)} | {Cell(p.Reason)} |");
        return sb.ToString();
    }
}

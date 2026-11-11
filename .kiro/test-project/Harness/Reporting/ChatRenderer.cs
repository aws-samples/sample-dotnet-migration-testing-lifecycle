using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace MigrationTests.Reporting;

/// <summary>
/// Deterministic chat views (design §5): plan, tests-authored, live-status, module-result, run-summary, fix-round, final.
/// Reads only files under the page dir; shows real values only ('None found.' rows for empty sources).
/// Every view (and every plan module part) starts with the 3-line header of <see cref="HeaderText"/>. Each report
/// section is ONE table (never split across messages; <see cref="OnePart"/>). Every table has a blank line before
/// and after it, and every cell escapes '|' (P4).
/// </summary>
public static class ChatRenderer
{
    public static readonly string[] Views = { "plan", "tests-authored", "live-status", "module-result", "run-summary", "fix-round", "final" };

    public const string TestsAuthoredTitle = "Test cases written and compiled (not run yet)";

    public const string TestsAuthoredExplainer =
        "Each module's Reference and Target test code was written from the plan and compiled. No tests have been run yet; results come next.";

    /// <summary>One rendered message: Suffix "" is the main part; plan parts use the module id.</summary>
    public sealed record Part(string Suffix, string Markdown);

    /// <summary>
    /// Renders one view (main part only). progressLine null/empty → computed from timings.json (LifecycleTimings.PhaseLine).
    /// Throws ReportInputException for a missing plan.json (all views) or a missing runs/Module{X} (module-result),
    /// ArgumentException for an unknown view or a missing module argument. "final" also rewrites reports/FinalReport.md.
    /// </summary>
    public static (string Markdown, string Summary) Render(string pageDir, string view, string? module, string? progressLine, DateTimeOffset? now = null)
    {
        var (parts, summary) = RenderParts(pageDir, view, module, progressLine, now);
        return (parts[0].Markdown, summary);
    }

    /// <summary>
    /// Renders every part of a view, in relay order. The plan view is split (P4): part "" is the overview
    /// (modules, states, NOT_TESTED) and part "A", "B", ... holds that module's steps. Other views have one part.
    /// </summary>
    public static (IReadOnlyList<Part> Parts, string Summary) RenderParts(string pageDir, string view, string? module, string? progressLine, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(pageDir)) throw new ArgumentException("--page-dir is required");
        view = (view ?? "").Trim().ToLowerInvariant();
        if (!Views.Contains(view)) throw new ArgumentException($"unknown view '{view}' (expected one of: {string.Join(", ", Views)})");
        var x = string.IsNullOrWhiteSpace(module) ? null : module.Trim().ToUpperInvariant();
        if (view == "module-result" && x == null) throw new ArgumentException("--module is required for view module-result");

        var d = ReportSources.Load(pageDir, now);
        if (d.Plan == null)
            throw new ReportInputException($"plan.json missing: {Path.Combine(d.PageDir, "state-map", "plan.json")}");
        if (view == "module-result" && !Directory.Exists(d.RunDir(x!)))
            throw new ReportInputException($"no results for Module {x}: {d.RunDir(x!)} does not exist");

        var line = string.IsNullOrWhiteSpace(progressLine) ? d.Lt.PhaseLine() : progressLine.Trim();
        if (view == "plan")
        {
            var (planParts, planSummary) = Plan(d, line);
            return (planParts.Select(p => OnePart(p.Markdown, p.Suffix)).ToList(), planSummary);
        }
        var (md, summary) = view switch
        {
            "tests-authored" => TestsAuthored(d, line),
            "live-status" => LiveStatus(d, line),
            "module-result" => ModuleResult(d, x!, line),
            "run-summary" => RunSummary(d, line),
            "fix-round" => FixRound(d, line),
            _ => Final(d, line)
        };
        return (new[] { OnePart(md, "") }, summary);
    }

    // ───────────────────────────── header (3 lines) ─────────────────────────────

    private static readonly System.Text.RegularExpressions.Regex PhaseRx = new(
        @"^Phase (?<k>\d+) of (?<n>\d+) \((?<name>.+?)\) - elapsed (?<el>[\d:]+) - estimated remaining (?<rem>[\d:]+)(?<wait> - waiting for you)?",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The 3-line header of every chat view (and of every plan module part):
    /// '### {title}' / '{Page} · Phase k of 5: {name}' / 'Run `{id}` · started d Mon HH:mm · elapsed mm:ss · about mm:ss left'
    /// ('waiting for you' at a gate, 'finished' when the lifecycle is done). Lines 2 and 3 are separate paragraphs.
    /// </summary>
    internal static string HeaderText(ReportSources d, string title, string progressLine)
    {
        var m = PhaseRx.Match(progressLine ?? "");
        var line2 = m.Success ? $"{d.Page} · Phase {m.Groups["k"].Value} of {m.Groups["n"].Value}: {m.Groups["name"].Value}" : d.Page;
        var bits = new List<string> { d.RunId is { } id ? $"Run `{id}`" : "Run id not recorded" };
        if (d.RunStartedAt is { } t) bits.Add("started " + t.ToString("d MMM HH:mm", CultureInfo.InvariantCulture));
        if (m.Success)
        {
            bits.Add("elapsed " + m.Groups["el"].Value);
            bits.Add(m.Groups["wait"].Success ? "waiting for you" : d.Lt.FinishedAt != null ? "finished" : $"about {m.Groups["rem"].Value} left");
        }
        else if (!string.IsNullOrWhiteSpace(progressLine))
        {
            var p = progressLine.Trim();
            bits.Add(char.ToLowerInvariant(p[0]) + p[1..]);
        }
        var nl = Environment.NewLine;
        return $"### {title}{nl}{nl}{line2}{nl}{nl}{string.Join(" · ", bits)}{nl}{nl}";
    }

    private static StringBuilder Head(ReportSources d, string title, string progressLine) => new(HeaderText(d, title, progressLine));

    // ───────────────────────────── one part per view ─────────────────────────────

    /// <summary>
    /// Final form of one chat message: line 1 ('### {title}') stays, every later section heading ('#'..'######')
    /// becomes '####' so sections sit under the title. Tables are never split: each report section is ONE table.
    /// </summary>
    internal static Part OnePart(string markdown, string suffix)
    {
        var lines = markdown.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        for (var i = 1; i < lines.Length; i++)
            if (lines[i].StartsWith('#')) lines[i] = "#### " + lines[i].TrimStart('#').Trim();
        var nl = Environment.NewLine;
        return new Part(suffix, ExtraBlankLines.Replace(string.Join(nl, lines).TrimEnd(), nl + nl) + nl);
    }

    private static readonly System.Text.RegularExpressions.Regex ExtraBlankLines =
        new(@"(\r?\n){3,}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Trims the end and collapses runs of blank lines to one (tables already carry their own blank lines).</summary>
    private static string Finish(StringBuilder sb) =>
        ExtraBlankLines.Replace(sb.ToString().TrimEnd(), Environment.NewLine + Environment.NewLine) + Environment.NewLine;

    private static (string, string) Done(StringBuilder sb, string summary) => (Finish(sb), summary);

    // ─────────────────────────────── 5.1 plan ───────────────────────────────

    private static int PlannedChecks(PlanModule m) => m.Steps.Sum(s => s.Changes.Count + s.StaysSame.Count + s.Checks.Count);

    private static (IReadOnlyList<Part>, string) Plan(ReportSources d, string line)
    {
        var plan = d.Plan!;
        var parts = new List<Part>();
        var sb = Head(d, "Test plan", line);
        sb.AppendLine($"Plan revision {plan.Revision}.");

        Md.Heading(sb, "### Modules");
        Md.Purpose(sb, "What each module tests: the user workflow, its steps and planned checks, whether it changes data (mutating modules run one at a time), and the page states whose styling is compared.");
        var rows = new List<string[]>();
        foreach (var m in plan.Modules)
        {
            var workflow = string.IsNullOrWhiteSpace(m.UserStory) ? m.Name : $"{m.Name}: {m.UserStory}";
            rows.Add(new[]
            {
                Md.Cell(m.Id), Md.Cell(workflow, Md.DescriptionCap), m.PlannedSteps.ToString(),
                m.Steps.Count == 0 ? "(no steps listed)" : PlannedChecks(m).ToString(),
                Md.Cell(d.DataEffectLabel(m.Id)), m.CssStates.Count == 0 ? "(none)" : Md.Cell(string.Join(", ", m.CssStates))
            });
        }
        Md.Table(sb, new[] { "Module", "Workflow (user story)", "Steps", "Planned checks", "Data effect", "CSS states" }, rows, true, "state-map/plan.json");
        foreach (var m in plan.Modules.Where(m => m.Steps.Count != m.PlannedSteps))
            sb.AppendLine($"WARNING: Module {m.Id} plannedSteps {m.PlannedSteps} but {m.Steps.Count} steps listed in plan.json");

        Md.Heading(sb, "### States");
        Md.Purpose(sb, "The page states the tests move through, how each one is reached, and what shows the page is in it.");
        if (plan.States.Count == 0) sb.AppendLine("(no states listed in plan.json)");
        else
        {
            var strows = plan.States.Select(s => new[]
            {
                Md.Cell(string.IsNullOrWhiteSpace(s.Name) ? s.Id : $"{s.Id} - {s.Name}"), Md.Cell(s.HowReached, Md.DescriptionCap),
                Md.Cell(string.Join("; ", s.Indicators), Md.DescriptionCap)
            }).ToList();
            Md.Table(sb, new[] { "State", "How reached", "Key indicators" }, strows, true, "state-map/plan.json");
        }

        Md.Heading(sb, "### NOT_TESTED");
        Md.Purpose(sb, "Items the automation cannot check reliably; a person verifies them by hand.");
        var nrows = plan.Modules.SelectMany(m => m.NotTested.Select(n => new[] { Md.Cell($"{m.Id}: {n.Item}", Md.DescriptionCap), Md.Cell(n.Reason, Md.DescriptionCap) })).ToList();
        Md.Table(sb, new[] { "Item", "Reason" }, nrows, true, "state-map/plan.json");
        if (plan.Modules.Count > 0)
            sb.AppendLine($"The steps of each module follow in separate messages: {string.Join(", ", plan.Modules.Select(m => "Module " + m.Id))}.");
        parts.Add(new Part("", Finish(sb)));

        foreach (var m in plan.Modules)
        {
            var mb = Head(d, $"Test plan - Module {m.Id}: {m.Name}", line);
            mb.AppendLine($"{m.PlannedSteps} steps, {d.DataEffectLabel(m.Id)}. The same steps run on the old page (Reference) and the new page (Target); every check must give the same result on both.");
            var srows = m.Steps.Select(s => new[] { Md.Cell(s.Id), Md.Cell(s.Action, Md.DescriptionCap), Md.Cell(CheckText(s, d.Labels), Md.DescriptionCap) }).ToList();
            Md.Table(mb, new[] { "Step", "What the user does", "What is checked" }, srows, false, "state-map/plan.json");
            parts.Add(new Part(m.Id, Finish(mb)));
        }

        var ro = plan.Modules.Where(m => d.DataEffectRaw(m.Id) == "read-only").Select(m => m.Id).ToList();
        var mu = plan.Modules.Where(m => d.DataEffectRaw(m.Id) != "read-only").Select(m => m.Id).ToList();
        var summary = $"Plan revision {plan.Revision}: {plan.Modules.Count} modules ({List(ro)} read-only; {List(mu)} mutating), " +
                      $"{plan.Modules.Sum(m => m.PlannedSteps)} steps, {plan.Modules.Sum(PlannedChecks)} planned checks.";
        return (parts, summary);
    }

    /// <summary>
    /// P3: plain-English checks without observed values. Uses plan.json checksText; without it falls back to the
    /// labels of the planned fields (the first token of each changes/staysSame/checks entry, never its values).
    /// </summary>
    internal static string CheckText(PlanStep s, FieldLabels labels)
    {
        var text = s.ChecksText.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        if (text.Count > 0)
            return string.Join(" ", text.Select(t => char.ToUpperInvariant(t[0]) + t[1..]).Select(t => t.EndsWith('.') ? t : t + "."));

        static string FieldOf(string entry)
        {
            var t = entry.Trim();
            var i = t.IndexOfAny(new[] { ' ', '\'', '"' });
            return i > 0 ? t[..i] : t;
        }
        List<string> Names(IEnumerable<string> entries) =>
            entries.Select(FieldOf).Where(f => f.Length > 0).Select(labels.Label).Distinct().ToList();
        var parts = new List<string>();
        var ch = Names(s.Changes);
        var ss = Names(s.StaysSame);
        var ck = Names(s.Checks);
        if (ch.Count > 0) parts.Add($"{JoinAnd(ch)} change{(ch.Count == 1 ? "s" : "")}");
        if (ss.Count > 0) parts.Add($"{JoinAnd(ss)} stay{(ss.Count == 1 ? "s" : "")} the same");
        if (ck.Count > 0) parts.Add($"{JoinAnd(ck)} {(ck.Count == 1 ? "is" : "are")} checked");
        return parts.Count == 0 ? "(no checks listed)" : "Checks: " + string.Join("; ", parts) + ".";
    }

    private static string JoinAnd(IReadOnlyList<string> w) =>
        w.Count switch { 0 => "", 1 => w[0], _ => string.Join(", ", w.Take(w.Count - 1)) + " and " + w[^1] };

    private static string List(IReadOnlyCollection<string> ids) => ids.Count == 0 ? "none" : string.Join(", ", ids);

    // ─────────────────────────── 5.2 tests-authored ──────────────────────────

    /// <summary>Why a test file has a step the plan does not list (P5 step-count notes).</summary>
    private static string ExtraStepReason(string name) => name switch
    {
        "NAV" => "NAV (page-open check)",
        "PRE" => "PRE (setup)",
        _ => name
    };

    private static List<string> StepNames(JsonNode? sys) =>
        (sys?["stepNames"] as JsonArray)?.Select(LifecycleTimings.Str).Where(s => s.Length > 0).ToList() ?? new List<string>();

    private static (string, string) TestsAuthored(ReportSources d, string line)
    {
        var sb = Head(d, TestsAuthoredTitle, line);
        sb.AppendLine(TestsAuthoredExplainer);
        var lt = d.Lt;
        var phase1Start = lt.HasLifecycle ? lt.Phases[1].FirstStart : null;
        var rows = new List<string[]>();
        var notes = new List<string>();
        var notReady = new List<string>();
        int ready = 0, written = 0, reused = 0, n = 0;
        foreach (var m in d.Plan!.Modules)
        {
            n++;
            var b = d.Build(m.Id);
            if (b == null)
            {
                rows.Add(new[] { m.Id, Md.Cell(m.Name, Md.DescriptionCap), $"- / - / {m.PlannedSteps}", "- / -", "Not compiled yet", "-" });
                notReady.Add($"{m.Id} (not compiled yet)");
                continue;
            }
            var refN = b["reference"];
            var tgtN = b["target"];
            var ok = LifecycleTimings.Str(b["result"]) == "PASS";
            var refWrite = LifecycleTimings.Date(refN?["lastWrite"]);
            var tgtWrite = LifecycleTimings.Date(tgtN?["lastWrite"]);
            var unchanged = phase1Start != null && refWrite != null && tgtWrite != null && refWrite < phase1Start && tgtWrite < phase1Start;

            string status, time;
            if (!ok)
            {
                var err = (b["errors"] as JsonArray)?.Select(LifecycleTimings.Str).FirstOrDefault(e => e.Length > 0);
                status = err != null ? "Compile failed: " + err : "Compile failed";
                notReady.Add($"{m.Id} (compile failed)");
            }
            else
            {
                status = unchanged ? "Unchanged from last run, compiled" : "Written + compiled";
                ready++;
            }
            var missing = new List<string>();
            foreach (var (label, node, plan) in new[] { ("Reference", refN, m.ReferenceTest), ("Target", tgtN, m.TargetTest) })
            {
                if (node == null) continue;
                var test = LifecycleTimings.Str(node["test"]) is { Length: > 0 } t ? t : plan;
                if (node["exists"] is JsonValue ev && ev.TryGetValue<bool>(out var exists) && !exists) missing.Add($"{label} test file missing");
                else if (node["methodFound"] is JsonValue mv && mv.TryGetValue<bool>(out var found) && !found) missing.Add($"{label} test method {test} not found");
            }
            if (missing.Count > 0) status += " (" + string.Join("; ", missing) + ")";

            if (unchanged) { time = "reused"; reused++; }
            else
            {
                var at = LifecycleTimings.Date(b["at"]);
                var offset = phase1Start != null && at != null ? (long)(at.Value - phase1Start.Value).TotalMilliseconds : -1;
                // A build long after the authoring phase (manual or fix-round rebuild) has no meaningful phase offset.
                time = offset < 0 ? "not measured"
                    : lt.Phases[1].Status != "running" && offset > lt.Phases[1].ActiveMs + 60000 ? "built later"
                    : LifecycleTimings.Mmss(offset);
                written++;
            }

            rows.Add(new[]
            {
                m.Id, Md.Cell(m.Name, Md.DescriptionCap),
                $"{Num(refN?["steps"])} / {Num(tgtN?["steps"])} / {m.PlannedSteps}",
                $"{Num(refN?["assertionCalls"])} / {Num(tgtN?["assertionCalls"])}",
                Md.Cell(status, Md.DescriptionCap), time
            });

            // Step-count notes: names in the test file that are not plan step ids (and planned ids missing from it).
            var refSteps = LifecycleTimings.Long(refN?["steps"], -1);
            var names = StepNames(refN);
            var planIds = m.Steps.Select(s => s.Id).ToList();
            if (names.Count > 0)
            {
                var extra = names.Where(nm => !planIds.Contains(nm)).ToList();
                var absent = planIds.Where(id => !names.Contains(id)).ToList();
                if (extra.Count > 0 || absent.Count > 0 || names.Count != m.PlannedSteps)
                {
                    var why = new List<string>();
                    if (extra.Count > 0) why.Add($"includes {JoinAnd(extra.Select(ExtraStepReason).ToList())}, not {(extra.Count == 1 ? "a planned step" : "planned steps")}");
                    if (absent.Count > 0) why.Add($"planned {JoinAnd(absent)} not found in the test");
                    notes.Add($"{m.Id}: {names.Count} steps vs planned {m.PlannedSteps}" + (why.Count > 0 ? ": " + string.Join("; ", why) : "") + ".");
                }
            }
            else if (refSteps >= 0 && refSteps != m.PlannedSteps)
                notes.Add($"{m.Id}: {refSteps} steps vs planned {m.PlannedSteps} (step names not recorded; rebuild the module to list them).");
        }
        Md.Table(sb, new[] { "Module", "Workflow", "Steps (Reference / Target / planned)", "Checks in code (Reference / Target)", "Status", "Time" }, rows, true, "runs/Module*/build.json");
        if (notes.Count > 0)
        {
            sb.AppendLine("Step counts:");
            sb.AppendLine();
            foreach (var note in notes) sb.AppendLine("- " + Md.Cell(note, Md.DescriptionCap * 2));
            sb.AppendLine();
        }
        sb.AppendLine("Checks in code = check calls written in the test file; the number of checks that ran appears in the module results.");
        sb.AppendLine();

        var detail = new List<string>();
        if (written > 0) detail.Add($"{written} written now");
        if (reused > 0) detail.Add($"{reused} reused from the previous run");
        var summary = $"{ready} of {n} modules ready to run" + (detail.Count > 0 ? $" ({string.Join(", ", detail)})" : "") +
                      (notReady.Count > 0 ? $"; not ready: {string.Join(", ", notReady)}" : "") + ".";
        sb.AppendLine(summary);
        return Done(sb, summary);
    }

    private static string Num(JsonNode? n)
    {
        var v = LifecycleTimings.Long(n, -1);
        return v < 0 ? "-" : v.ToString(CultureInfo.InvariantCulture);
    }

    // ───────────────────────────── live-status (P6) ─────────────────────────────

    private static (string, string) LiveStatus(ReportSources d, string line)
    {
        var sb = Head(d, "Run status", line);
        var run = SelectRun(d);
        var runStart = LifecycleTimings.Date(run?["startedAt"]);
        var runModules = (run?["modules"] as JsonArray)?.Select(LifecycleTimings.Str).Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToList();
        var ids = d.PlannedIds();
        if (runModules is { Count: > 0 }) ids = ids.Where(runModules.Contains).Concat(runModules.Where(m => !ids.Contains(m))).ToList();
        var jobs = (d.Status?["currentRun"]?["jobs"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();

        string Cell(string x, string sys, out bool finished, out bool running)
        {
            var job = jobs.FirstOrDefault(j => LifecycleTimings.Str(j["module"]).Equals(x, StringComparison.OrdinalIgnoreCase) && LifecycleTimings.Str(j["system"]) == sys);
            running = job != null;
            finished = false;
            if (job != null)
            {
                var i = LifecycleTimings.Long(job["stepIndex"]);
                var t = LifecycleTimings.Long(job["stepTotal"]);
                return i > 0 && t > 0 ? $"running (step {i} of {t})" : "starting";
            }
            var node = d.Status?["modules"]?[x]?[sys.ToLowerInvariant()];
            var st = LifecycleTimings.Str(node?["status"]);
            var executed = LifecycleTimings.Date(node?["executedAt"]);
            if (node == null || st is "" or "NOT_RUN" || (runStart != null && executed != null && executed < runStart)) return "queued";
            finished = true;
            return $"{StepFrac(node)} {st}";
        }

        var rows = new List<string[]>();
        int done = 0;
        foreach (var x in ids)
        {
            var r = Cell(x, "Reference", out var rf, out var rr);
            var t = Cell(x, "Target", out var tf, out var tr);
            string status;
            if (rf && tf)
            {
                done++;
                var s = d.Summary(x);
                status = s == null ? "done" : $"done: {Count(s.OpenFindings, "open functional gap")}, {Count(s.ActionableCssDiffs, "CSS issue")}";
            }
            else status = rr || tr || rf || tf ? "running" : "queued";
            if (d.HarnessError(x) is { } he) status = Md.Cell("harness error: " + he);
            rows.Add(new[] { x, r, t, status });
        }
        Md.Purpose(sb, "Progress of each module on the old page (Reference) and the new page (Target): steps passed of the total, then the open issues once both sides finished.");
        Md.Table(sb, new[] { "Module", "Old page (Reference)", "New page (Target)", "Status" }, rows, true, "status.json");
        sb.AppendLine("Full module results follow in plan order once every module has finished.");
        var summary = $"Run progress: {done} of {ids.Count} modules finished ({string.Join(", ", rows.Select(rw => $"{rw[0]} {(rw[3].StartsWith("done", StringComparison.Ordinal) ? "done" : rw[3])}"))}).";
        return Done(sb, summary);
    }

    // ─────────────────────────── 5.3 module-result ───────────────────────────

    private static (string, string) ModuleResult(ReportSources d, string x, string line)
    {
        var sb = Head(d, $"Module {x}: {d.ModuleName(x)}", line);
        var effect = d.DataEffectRaw(x);
        sb.AppendLine(effect == "read-only"
            ? "Data effect: read-only (ran in parallel)"
            : effect == "mutating"
                ? "Data effect: mutating (serialized; service restarted after Reference and Target)"
                : "Data effect: mutating (default: not set in plan) (serialized; service restarted after Reference and Target)");

        var reference = d.Results(x, "Reference");
        var target = d.Results(x, "Target");
        var parity = $"reports/Module{x}_Parity.md";
        foreach (var (title, r) in new[] { ("Reference", reference), ("Target", target) })
        {
            Md.Heading(sb, $"### {title} Step Report");
            Md.Purpose(sb, title == "Reference"
                ? "What the test did on the old page (Reference), step by step; PASS means every check of the step held."
                : "The same steps on the new page (Target); a FAIL row starts with what differed from the old page.");
            if (r == null) sb.AppendLine("(not run)");
            else
            {
                var rows = r.Steps.Select(s =>
                {
                    var st = ModuleReportGenerator.StepStatus(s);
                    return new[] { Md.Cell(s.Step), st, StepDescription(d.Labels, s, st, title == "Target") };
                }).ToList();
                if (r.Aborted) rows.Add(new[] { "-", "FAIL", Md.Cell("ABORTED: " + r.AbortReason, Md.DescriptionCap) });
                Md.Table(sb, new[] { "Step", "Status", "Description" }, rows, true, parity);
            }
        }

        Md.Heading(sb, "### Performance");
        Md.Purpose(sb, PerformancePurpose);
        var prow = new List<string[]>();
        foreach (var name in ModuleReportGenerator.StepNames(reference, target))
            prow.Add(new[] { Md.Cell(name), ModuleReportGenerator.Ms(reference, name), ModuleReportGenerator.Ms(target, name) });
        var total = prow.Count > 0
            ? new[] { "Total", reference == null ? "-" : ModuleReportGenerator.SumMs(reference).ToString(), target == null ? "-" : ModuleReportGenerator.SumMs(target).ToString() }
            : null;
        Md.Table(sb, new[] { "Step", "Old page (Reference) ms", "New page (Target) ms" }, prow, true, $"reports/Module{x}_Performance.md", total);

        var s = d.Summary(x);
        Md.Heading(sb, "### Functional Gaps");
        Md.Purpose(sb, GapsPurpose);
        if (s == null) sb.AppendLine($"(summary.json missing for Module {x}: findings were not classified)");
        else
        {
            var grows = s.Findings.OrderBy(f => Md.SevRank(f.Severity)).Select(f => new[]
            {
                Md.Cell(f.Id), f.Actionable ? f.Severity : f.Severity + " (info)", Md.Cell(GapWhat(d.Labels, f)),
                Md.Cell(FieldLabels.ValueText(f.Field, f.Reference)), Md.Cell(FieldLabels.ValueText(f.Field, f.Target))
            }).ToList();
            Md.Table(sb, new[] { "ID", "Severity", "What", "Old page (Reference)", "New page (Target)" }, grows, true, parity);
        }

        Md.Heading(sb, "### CSS Diffs");
        if (s == null) sb.AppendLine($"(summary.json missing for Module {x}: CSS was not compared)");
        else
        {
            // Page-level groups seen in this module: top ChatTop actionable (severity, then element count), rest in CSS_Groups.md.
            var mine = d.CssGroups().Where(g => g.Actionable && g.Modules.Contains(x, StringComparer.OrdinalIgnoreCase))
                .OrderBy(g => Md.SevRank(g.Severity)).ThenByDescending(g => g.ElementCount).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            Md.Purpose(sb, CssPurpose);
            var crows = mine.Take(d.ChatTopGroups).Select(g => new[]
            {
                Md.Cell(ModuleReportGenerator.IssueOf(g), Md.IssueCap), g.Id, Md.Cell(ModuleReportGenerator.GroupElements(g)),
                Md.Cell(FieldLabels.PropertyText(g.Property)), Md.Cell(g.Reference), Md.Cell(g.Target),
                g.Severity, Md.Cell(ModuleReportGenerator.GroupSeenIn(g))
            }).ToList();
            Md.Table(sb, new[] { "Issue", "Group", "Elements", "Property", "Reference", "Target", "Severity", "Seen in" }, crows, true, $"reports/Module{x}_CSS_Diff.md");
            if (mine.Count > crows.Count) sb.AppendLine($"{mine.Count - crows.Count} more in {Md.PathCode("reports/CSS_Groups.md")}");
            sb.AppendLine($"{s.CssDiffs.Count(c => c.Accepted)} accepted differences in {Md.PathCode($"reports/Module{x}_CSS_Diff.md")}");
            var byProfile = s.CssDiffs.Count(c => c.AcceptedBy?.StartsWith("profile ", StringComparison.Ordinal) == true);
            if (!string.IsNullOrEmpty(s.StackProfile))
                sb.AppendLine($"Stack profile: {s.StackProfile}, {byProfile} rows accepted by profile");
        }
        var uncompared = d.UncomparedCssStates(x);
        if (uncompared.Count > 0)
            sb.AppendLine($"Not compared: no {Md.PathCode("state-map/css-map.json")} elements for state(s) {string.Join(", ", uncompared)}, so these states are not a clean result.");

        if (s != null && s.ReferenceDefects.Count > 0)
        {
            // Baseline problems (content checks on the Reference); listed, never counted in Target parity.
            Md.Heading(sb, "### Reference Defects");
            Md.Purpose(sb, ReferenceDefectsPurpose);
            var rrows = s.ReferenceDefects.Select(r => new[]
            {
                Md.Cell(r.Id), r.Severity, Md.Cell(string.Join(", ", r.Steps.DefaultIfEmpty(r.Step))), Md.Cell(ModuleReportGenerator.ReferenceDefectPlain(r), Md.DescriptionCap)
            }).ToList();
            Md.Table(sb, new[] { "ID", "Severity", "Step", "Text" }, rrows, true, parity);
            sb.AppendLine("Reference defects are baseline issues: they do not fail Target parity.");
        }
        sb.AppendLine();

        sb.AppendLine(ModuleMeaning(s, target));
        sb.AppendLine();
        var text = ModuleSummaryText(x, reference, target, s, uncompared);
        sb.AppendLine("Summary: " + text);
        return Done(sb, $"Module {x}: {text}");
    }

    internal const string PerformancePurpose =
        "Time per step in milliseconds (ms), old page vs new page. Informational only: speed never fails a module.";

    internal const string GapsPurpose =
        "Behavior or data that differs between the old page (Reference) and the new page (Target). HIGH = different data or behavior, MEDIUM = different message, LOW = formatting only; (info) rows are not counted.";

    internal const string CssPurpose =
        "Styling and layout differences still open, one row per fix (most severe first). The Issue column says what differs in plain English; the other columns are the technical detail.";

    internal const string ReferenceDefectsPurpose =
        "Problems found on the old page (Reference) itself, such as server code shown as text. They are recorded for the record and never copied to or counted against the new page.";

    /// <summary>
    /// "What this means / what to do" line of a module result (deterministic from the summary): parity reached, or what is
    /// still open (functional gaps, CSS groups owned here, CSS rows counted in another module's group, failed steps).
    /// </summary>
    internal static string ModuleMeaning(ModuleSummary? s, RunResultsFile? target)
    {
        if (s == null) return "What this means: the results were not classified (summary.json missing). What to do: re-run the module's report.";
        if (s.TargetStatus == "PASS")
            return "What this means: the new page (Target) matches the old page (Reference) for this module. What to do: nothing.";
        var open = new List<string>();
        if (target == null) open.Add("the new page test did not run");
        else if (target.Totals.StepsFail > 0 || target.Aborted) open.Add($"{Count(target.Totals.StepsFail, "step")} failed on the new page");
        if (s.OpenFindings > 0) open.Add($"{Count(s.OpenFindings, "functional gap")} {(s.OpenFindings == 1 ? "is" : "are")} open");
        var refLarger = ModuleReportGenerator.ReferenceLargerGroups(s);
        if (s.ActionableCssDiffs > 0)
            open.Add($"{Count(s.ActionableCssDiffs, "styling/layout issue")} {(s.ActionableCssDiffs == 1 ? "is" : "are")} open" + OldPageDefects(refLarger, s.ActionableCssDiffs));
        var shared = s.CssDiffs.Count(c => c.Actionable);
        if (s.ActionableCssDiffs == 0 && shared > 0) open.Add($"{Count(shared, "styling row")} of this module belong to styling issues counted in another module");
        if (open.Count == 0) open.Add("the module did not meet every pass rule (see the parity report)");
        return $"What this means: the new page (Target) is not at parity yet for this module: {JoinAnd(open)}. " +
               WhatToDo(refLarger > 0 && s.ActionableCssDiffs > 0, refLarger >= s.ActionableCssDiffs && open.Count == 1);
    }

    private const string OldPageDefectAdvice =
        "confirm the old-page defects on the old page and accept them in state-map/css-map.json; never copy them into the new page";

    /// <summary>What-to-do sentence; names the old-page defects (counted Reference-larger groups) separately when there are any.</summary>
    internal static string WhatToDo(bool hasOldPageDefects, bool onlyOldPageDefects) =>
        !hasOldPageDefects
            ? "What to do: fix these in the new page (the fix round does this automatically), or accept an intended difference in state-map/css-map.json."
            : onlyOldPageDefects
                ? $"What to do: {OldPageDefectAdvice}."
                : $"What to do: fix the other issues in the new page (the fix round does this automatically) or accept an intended difference in state-map/css-map.json; {OldPageDefectAdvice}.";

    /// <summary>" (2 of them are old-page defects to accept or confirm, not to copy)" for counted Reference-larger groups.</summary>
    internal static string OldPageDefects(int refLarger, int total) =>
        refLarger <= 0 ? ""
        : refLarger == total && total == 1 ? " (it is an old-page defect to accept or confirm, not to copy)"
        : refLarger == total ? $" (all of them are old-page defects to accept or confirm, not to copy)"
        : $" ({refLarger} of them {(refLarger == 1 ? "is an old-page defect" : "are old-page defects")} to accept or confirm, not to copy)";

    private static string Count(long n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Gap "What" cell: the field label with the step, e.g. "Pager text (Step1)".</summary>
    internal static string GapWhat(FieldLabels labels, Finding f) =>
        string.IsNullOrWhiteSpace(f.Field) ? f.Step : string.IsNullOrWhiteSpace(f.Step) ? labels.Label(f.Field) : $"{labels.Label(f.Field)} ({f.Step})";

    /// <summary>
    /// "Target 13/13 steps passed (55/55 assertions), module FAIL (styling/layout open)": the step count never reads as
    /// failed steps; a non-PASS Target says what keeps it open. The Reference result is the baseline.
    /// </summary>
    internal static string SystemText(string name, RunResultsFile? r, string? status, ModuleSummary? s = null)
    {
        if (r == null) return $"{name} not run";
        var st = status ?? (ModuleReportGenerator.ReferencePasses(r) ? "PASS" : "FAIL");
        var core = $"{name} {r.Totals.StepsPass}/{r.Steps.Count} steps passed ({r.Totals.AssertionsPassed}/{r.Totals.AssertionsTotal} assertions)";
        if (name == "Reference") return $"{core}, baseline {st}";
        var why = new List<string>();
        if (st != "PASS")
        {
            var failed = r.Steps.Count - r.Totals.StepsPass;
            if (r.Aborted) why.Add("run aborted");
            else if (failed > 0) why.Add($"{Count(failed, "step")} failed");
            if (s != null && s.OpenFindings > 0) why.Add("functional gaps open");
            if (s != null && (s.ActionableCssDiffs > 0 || s.CssDiffs.Any(c => c.Actionable))) why.Add("styling/layout open");
        }
        return $"{core}, module {st}{(why.Count > 0 ? $" ({string.Join(", ", why)})" : "")}";
    }

    /// <summary>
    /// Step description cell in plain English (P7). FAIL rows lead with the mismatch ("Rows shown is 5, expected 10
    /// (Reference)"); long text is cut at a sentence end.
    /// </summary>
    internal static string StepDescription(FieldLabels labels, RunStep s, string status, bool isTarget) =>
        Md.Cell(labels.Describe(s, status, isTarget, summarizeLists: true), Md.DescriptionCap);

    private static string ModuleSummaryText(string x, RunResultsFile? reference, RunResultsFile? target, ModuleSummary? s, IReadOnlyList<string> uncomparedCss)
    {
        var parts = new List<string>
        {
            SystemText("Reference", reference, s?.ReferenceStatus),
            SystemText("Target", target, s?.TargetStatus, s)
        };
        if (s != null)
        {
            parts.Add($"{s.Findings.Count} gaps (H {s.Findings.Count(f => f.Severity == "HIGH")} / M {s.Findings.Count(f => f.Severity == "MEDIUM")} / L {s.Findings.Count(f => f.Severity == "LOW")})");
            var notCompared = uncomparedCss.Count > 0 ? $", CSS not compared for {string.Join(", ", uncomparedCss)}" : "";
            parts.Add($"{s.ActionableCssDiffs} actionable CSS groups ({s.CssDiffs.Count(c => c.Actionable)} raw rows actionable, {s.CssDiffsTotal} total, {s.CssDiffs.Count(c => c.Accepted)} accepted{notCompared})");
            if (s.ReferenceDefectCount > 0) parts.Add($"{s.ReferenceDefectCount} Reference defects");
        }
        else parts.Add("gaps and CSS not classified (summary.json missing)");
        var refS = reference == null ? "-" : Md.Seconds(ModuleReportGenerator.SumMs(reference));
        var tgtS = target == null ? "-" : Md.Seconds(ModuleReportGenerator.SumMs(target));
        return string.Join("; ", parts) + $"; Reference {refS} vs Target {tgtS}.";
    }

    // ──────────────────────────── 5.4 run-summary ────────────────────────────

    /// <summary>
    /// [review M1] timings.runs[] entry whose runId equals status.currentRun.runId when that entry is a run-page run
    /// (or has no action); otherwise (e.g. currentRun is a report or rerun-target run) the last run-page entry;
    /// else the last entry.
    /// </summary>
    internal static JsonObject? SelectRun(ReportSources d)
    {
        var runs = (d.Timings?["runs"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        if (runs.Count == 0) return null;
        static bool IsRunPage(JsonObject r) => LifecycleTimings.Str(r["action"]) is "" or "run-page";
        var current = LifecycleTimings.Str(d.Status?["currentRun"]?["runId"]);
        if (current.Length > 0)
        {
            var match = runs.LastOrDefault(r => LifecycleTimings.Str(r["runId"]) == current);
            if (match != null && IsRunPage(match)) return match;
        }
        return runs.LastOrDefault(IsRunPage) ?? runs[^1];
    }

    private static string StepFrac(JsonNode? sys)
    {
        if (sys == null) return "NOT_RUN";
        var status = LifecycleTimings.Str(sys["status"]);
        var pass = LifecycleTimings.Long(sys["stepsPass"]);
        var total = pass + LifecycleTimings.Long(sys["stepsFail"]) + LifecycleTimings.Long(sys["stepsNotTested"]);
        if (status == "NOT_RUN" || total == 0) return "NOT_RUN";
        return $"{pass}/{total}";
    }

    private static (string, string) RunSummary(ReportSources d, string line)
    {
        var sb = Head(d, "Run summary", line);
        var run = SelectRun(d);
        var runModules = (run?["modules"] as JsonArray)?.Select(LifecycleTimings.Str).Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToList();
        var ids = d.PlannedIds();
        if (runModules is { Count: > 0 }) ids = ids.Where(runModules.Contains).Concat(runModules.Where(m => !ids.Contains(m))).ToList();

        var rows = new List<string[]>();
        int refPass = 0, tgtPass = 0, findings = 0, css = 0;
        foreach (var x in ids)
        {
            var stm = d.Status?["modules"]?[x];
            var s = d.Summary(x);
            var r = d.Results(x, "Reference");
            var t = d.Results(x, "Target");
            if ((s?.ReferenceStatus ?? LifecycleTimings.Str(stm?["reference"]?["status"])) == "PASS") refPass++;
            if (s?.TargetStatus == "PASS") tgtPass++;
            findings += s?.Findings.Count ?? 0;
            css += s?.ActionableCssDiffs ?? 0;
            rows.Add(new[]
            {
                x, StepFrac(stm?["reference"]), StepFrac(stm?["target"]),
                s == null ? "-" : $"{s.Findings.Count} ({s.Findings.Count(f => f.Severity == "HIGH")}/{s.Findings.Count(f => f.Severity == "MEDIUM")}/{s.Findings.Count(f => f.Severity == "LOW")})",
                s?.ActionableCssDiffs.ToString() ?? "-",
                r == null ? "-" : ModuleReportGenerator.SumMs(r).ToString(),
                t == null ? "-" : ModuleReportGenerator.SumMs(t).ToString()
            });
        }
        Md.Purpose(sb, "One row per module: steps passed on the old page (Reference) and the new page (Target), open functional gaps by severity (HIGH/MEDIUM/LOW), open styling/layout issues (CSS groups), and run time in milliseconds.");
        Md.Table(sb, new[] { "Module", "Old page (Reference) steps passed", "New page (Target) steps passed", "Functional gaps (H/M/L)", "CSS issues", "Old page ms", "New page ms" }, rows, true, "status.json");

        // Schedule
        var tests = (run?["tests"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        var ro = tests.Where(t => LifecycleTimings.Str(t["batch"]) == "read-only").Select(t => LifecycleTimings.Str(t["module"])).Distinct().ToList();
        var mu = tests.Where(t => LifecycleTimings.Str(t["batch"]) == "mutating").Select(t => LifecycleTimings.Str(t["module"])).Distinct().ToList();
        var restarts = (run?["restarts"] as JsonArray)?.OfType<JsonObject>().Select(r => LifecycleTimings.Long(r["ms"])).ToList() ?? new List<long>();
        if (ro.Count == 0 && mu.Count == 0) sb.AppendLine("Schedule: not recorded for this run (no batch information in timings.json).");
        else
        {
            var max = LifecycleTimings.Long(run?["maxParallel"], 0);
            var restartText = restarts.Count == 0 ? "no service restarts" :
                $"{restarts.Count} service restart{(restarts.Count == 1 ? "" : "s")} ({string.Join(", ", restarts.Select(Md.Seconds))})";
            sb.AppendLine($"Schedule: parallel read-only {List(ro)}{(max > 0 ? $" (max {max})" : "")}; serialized mutating {List(mu)} with {restartText}.");
        }

        // Wall time [review M1]: finishedAt - startedAt, else env LIFECYCLE_RUN_WALL_MS for a run still in progress.
        string wall = "not measured";
        if (run != null)
        {
            var started = LifecycleTimings.Date(run["startedAt"]);
            var finished = LifecycleTimings.Date(run["finishedAt"]);
            if (started != null && finished != null) wall = LifecycleTimings.Mmss((long)(finished.Value - started.Value).TotalMilliseconds);
            else if (long.TryParse(Environment.GetEnvironmentVariable("LIFECYCLE_RUN_WALL_MS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var wms) && wms >= 0)
                wall = LifecycleTimings.Mmss(wms);
        }
        var extras = new List<string>();
        if (run?["buildMs"] != null) extras.Add($"build {LifecycleTimings.Long(run["buildMs"]) / 1000}s");
        if (run?["preflightMs"] != null) extras.Add($"preflight {LifecycleTimings.Long(run["preflightMs"]) / 1000}s");

        // Harness errors: status.modules[*].harnessError + render-failed status events of this run.
        var errors = new List<string>();
        foreach (var x in d.ModuleIds())
            if (d.HarnessError(x) is { } he) errors.Add($"Module {x}: {he}");
        var runId = LifecycleTimings.Str(run?["runId"]);
        foreach (var ev in (d.Status?["events"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            if (LifecycleTimings.Str(ev["type"]) != "render-failed") continue;
            var evRun = LifecycleTimings.Str(ev["runId"]);
            if (evRun.Length > 0 && runId.Length > 0 && evRun != runId) continue;
            var mod = LifecycleTimings.Str(ev["module"]);
            errors.Add($"render {LifecycleTimings.Str(ev["view"])}{(mod.Length > 0 ? " " + mod : "")} failed");
        }
        sb.AppendLine($"Phase 3 ({LifecycleTimings.PhaseNames[2]}) wall time: {wall}{(extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "")}. Harness errors: {(errors.Count == 0 ? "none" : Md.Cell(string.Join("; ", errors), Md.DescriptionCap * 2))}.");

        var n = ids.Count;
        var summary = $"Run: {n} modules in {wall}; Ref PASS {refPass}/{n}; Target PASS {tgtPass}/{n}; {findings} findings; {css} actionable CSS groups.";
        return Done(sb, summary);
    }

    // ───────────────────────────── 5.5 fix-round ─────────────────────────────

    private static (string, string) FixRound(ReportSources d, string line)
    {
        var round = d.FixRound;
        var sb = Head(d, $"Fix round {round}", line);
        sb.AppendLine($"Round {round} of at most {d.MaxRounds}.");
        var fixes = d.Fixes.Where(f => f.Round == round).ToList();
        var rows = new List<string[]>();
        var resolved = 0;
        foreach (var f in fixes)
        {
            var s = d.Summary(f.Module);
            var ids = f.FindingIds;
            bool isResolved;
            if (s == null) isResolved = false;
            else
            {
                var open = new HashSet<string>(s.Findings.Select(x => x.Id), StringComparer.Ordinal);
                isResolved = ids.Count > 0 && ids.All(id => id.StartsWith("css:", StringComparison.Ordinal)
                    ? !FinalReportBuilder.CssStillActionable(d, f.Module, id)
                    : !open.Contains(id));
            }
            if (isResolved) resolved++;
            rows.Add(new[]
            {
                Md.Cell(string.Join(", ", ids)), Md.Cell(f.Module), Md.Cell(FixWhat(d, f), Md.IssueCap), Md.Cell(f.Kind), Md.Cell(f.File, Md.DescriptionCap), Md.Cell(f.Change, Md.DescriptionCap),
                $"`{Md.Cell(f.Before, Md.CodeCap)}` -> `{Md.Cell(f.After, Md.CodeCap)}`",
                isResolved ? "RESOLVED" : "STILL OPEN"
            });
        }
        Md.Purpose(sb, "Each edit made in this fix round: what it was meant to fix, the file and change, and whether re-running the new page (Target) resolved it. Kind: app = new page code, test-* = test code.");
        Md.Table(sb, new[] { "Finding", "Module", "What", "Kind", "File", "Change", "Before -> After", "Rerun result" }, rows, true, "fixes.json");

        var hist = (d.Status?["fixLoop"]?["history"] as JsonArray)?.OfType<JsonObject>()
            .LastOrDefault(h => LifecycleTimings.Long(h["round"], -1) == round);
        var totals = new List<string>();
        long before = 0, after = 0;
        if (hist?["modules"] is JsonObject hm)
            foreach (var (mod, v) in hm)
            {
                var fb = LifecycleTimings.Long(v?["openFindingsBefore"]);
                var fa = v?["openFindingsAfter"] != null ? LifecycleTimings.Long(v["openFindingsAfter"]) : (d.Summary(mod)?.OpenFindings ?? fb);
                var cb = LifecycleTimings.Long(v?["actionableCssBefore"]);
                var ca = v?["actionableCssAfter"] != null ? LifecycleTimings.Long(v["actionableCssAfter"]) : (d.Summary(mod)?.ActionableCssDiffs ?? cb);
                before += fb;
                after += fa;
                var tgt = d.Status?["modules"]?[mod]?["target"];
                var tgtText = tgt == null ? "Target not run" : $"Target {StepFrac(tgt)} steps passed, module {FixRoundVerdict(tgt, fa, ca)}";
                totals.Add($"{mod} findings {fb} -> {fa}, CSS {cb} -> {ca}, {tgtText}");
            }
        sb.AppendLine("Module totals: " + (totals.Count == 0 ? "(none)" : string.Join("; ", totals)) + ".");

        var summary = $"Round {round}: {resolved}/{rows.Count} edits resolved their findings; open findings {before} -> {after}.";
        return Done(sb, summary);
    }

    /// <summary>
    /// Module verdict after a fix round, as in <see cref="SystemText"/>: status.json's Target status is the step result,
    /// so open gaps or CSS issues turn a step PASS into "FAIL (styling/layout open)" (never a false module PASS).
    /// </summary>
    private static string FixRoundVerdict(JsonNode tgt, long openFindings, long actionableCss)
    {
        var st = LifecycleTimings.Str(tgt["status"]);
        var why = new List<string>();
        var failed = LifecycleTimings.Long(tgt["stepsFail"]);
        if (failed > 0) why.Add($"{Count(failed, "step")} failed");
        if (openFindings > 0) why.Add("functional gaps open");
        if (actionableCss > 0) why.Add("styling/layout open");
        if (st == "PASS" && why.Count > 0) st = "FAIL";
        return st != "PASS" && why.Count > 0 ? $"{st} ({string.Join(", ", why)})" : st;
    }

    /// <summary>Plain-English subject of a fix: the labels of its finding fields ("Pager text (Step1)"), CSS keys as-is.</summary>
    private static string FixWhat(ReportSources d, FixEntry f)
    {
        var whats = new List<string>();
        var earlier = 0;
        foreach (var id in f.FindingIds)
        {
            if (id.StartsWith(FinalReportBuilder.CssGroupPrefix, StringComparison.Ordinal))
            {
                var key = id[FinalReportBuilder.CssGroupPrefix.Length..];
                var g = d.CssGroups().FirstOrDefault(x => x.Key == key);
                if (g == null) { earlier++; continue; }
                whats.Add($"CSS group {g.Id}: {ModuleReportGenerator.IssueOf(g).TrimEnd('.')}");
                continue;
            }
            if (id.StartsWith("css:", StringComparison.Ordinal)) { whats.Add("CSS " + id[4..]); continue; }
            var finding = d.Summary(f.Module)?.Findings.FirstOrDefault(x => x.Id == id) ?? d.PriorFinding(f.Module, f.Round, id);
            if (finding != null) { whats.Add(GapWhat(d.Labels, finding)); continue; }
            var parts = id.Split('-', 3);
            whats.Add(parts.Length == 3 ? $"{d.Labels.Label(parts[2])} ({parts[1]})" : id);
        }
        // Group keys no longer in css_groups.json (fixed, or regrouped since): one phrase, keys stay in the Finding column.
        if (earlier > 0) whats.Add($"{earlier} CSS issue{(earlier == 1 ? "" : "s")} from an earlier run, no longer in the current CSS groups");
        return string.Join("; ", whats.Distinct());
    }

    // ─────────────────────────────── 5.6 final ───────────────────────────────

    private static (string, string) Final(ReportSources d, string line)
    {
        var sb = Head(d, "Final report", line);
        var sections = FinalReportBuilder.BuildSections(d, capped: true);
        sb.Append(sections.Markdown);
        var path = Path.Combine(d.PageDir, "reports", "FinalReport.md");
        ModuleReportGenerator.WriteText(path, FinalReportBuilder.BuildReport(d)); // §5.7: same data as the chat view
        sb.AppendLine();
        var docs = new[] { "FinalReport.pdf", "FinalReport.docx", "TestCaseResults.pdf", "TestCaseResults.docx" }
            .Select(f => Path.Combine(d.PageDir, "reports", f)).Where(File.Exists).ToList();
        // Paths as inline code: bare Windows paths lose the '\' before '.kiro' in markdown.
        var docRows = new List<string[]> { new[] { "Full report (markdown)", Md.PathCode(path) } };
        docRows.AddRange(docs.Select(p => new[] { Path.GetFileName(p), Md.PathCode(p) }));
        Md.Table(sb, new[] { "Document", "Path" }, docRows, false, "reports");
        return Done(sb, sections.Summary);
    }
}

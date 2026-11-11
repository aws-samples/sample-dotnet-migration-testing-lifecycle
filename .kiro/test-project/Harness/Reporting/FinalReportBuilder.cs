using System.Text;
using System.Text.Json.Nodes;

namespace MigrationTests.Reporting;

/// <summary>Result of the final sections (shared by the chat "final" view and FinalReport.md).</summary>
public sealed class FinalSections
{
    public string Markdown { get; init; } = "";
    /// <summary>PASS | FAIL.</summary>
    public string Overall { get; init; } = "FAIL";
    /// <summary>"Final: {Overall}; {a}/{n} Ref PASS, {b}/{n} Target PASS, {f} open findings; total {elapsed} of 15:00."</summary>
    public string Summary { get; init; } = "";
}

/// <summary>
/// Deterministic writer of reports/FinalReport.md (design §5.6 sections 1-7 uncapped + §5.7 per-module
/// parity content, ## UNRESOLVED, ## NOT_RUN). Reads only files; no LLM content. Tolerates missing
/// timings.json, fixes.json and status.json ("No lifecycle", design §4).
/// </summary>
public static class FinalReportBuilder
{
    public const string RelativePath = "reports/FinalReport.md";

    /// <summary>Called by ModuleReportGenerator.Generate(pageDir, "ALL") with the summaries it just produced.</summary>
    public static string Write(string pageDir, IReadOnlyList<ModuleSummary> summaries, PlanFile? plan, JsonNode? status, DateTimeOffset? now = null)
        => WriteReport(ReportSources.From(pageDir, plan, status, summaries, now));

    /// <summary>Rebuilds FinalReport.md from the existing runs/Module*/summary.json files (render -View final).</summary>
    public static string WriteFromDisk(string pageDir, DateTimeOffset? now = null) => WriteReport(ReportSources.Load(pageDir, now));

    /// <summary>Full (uncapped) FinalReport.md markdown.</summary>
    public static string Build(string pageDir, DateTimeOffset? now = null) => BuildReport(ReportSources.Load(pageDir, now));

    private static string WriteReport(ReportSources d)
    {
        var path = Path.Combine(d.PageDir, "reports", "FinalReport.md");
        ModuleReportGenerator.WriteText(path, BuildReport(d));
        return path;
    }

    internal static string BuildReport(ReportSources d)
    {
        var sb = new StringBuilder();
        // The H1 is also the PDF/DOCX title and metadata title (ReportDocuments.WriteFinal): it names the run.
        sb.AppendLine(d.RunLabel is { } run ? $"# Final Report - {d.Page} - {run}" : $"# Final Report - {d.Page}");
        sb.AppendLine();
        if (d.RunLabel != null)
        {
            sb.AppendLine(d.RunDetails);
            sb.AppendLine();
        }
        sb.AppendLine($"Generated: {d.Now:yyyy-MM-dd HH:mm:ss} by the Reports tool from runs/Module*/summary.json, status.json, timings.json and fixes.json (no hand-written content).");
        sb.AppendLine();
        sb.AppendLine(d.Lt.PhaseLine());
        sb.AppendLine();
        var sections = BuildSections(d, capped: false);
        sb.Append(sections.Markdown);
        sb.AppendLine();

        // Per-module parity content (existing BuildParity, headings demoted one level).
        foreach (var x in d.ModuleIds())
        {
            sb.AppendLine($"## Module {x} - {d.ModuleName(x)}");
            sb.AppendLine();
            var s = d.Summary(x);
            if (s == null)
            {
                sb.AppendLine($"NOT_RUN: runs/Module{x}/summary.json missing (module reports were not generated).");
                sb.AppendLine();
                continue;
            }
            var parity = ModuleReportGenerator.BuildParity(d.Page, x, d.Results(x, "Reference"), d.Results(x, "Target"), s, d.Labels);
            sb.AppendLine(DemoteParity(parity));
            sb.AppendLine();
        }

        // UNRESOLVED
        sb.AppendLine("## UNRESOLVED");
        sb.AppendLine();
        var open = d.ModuleIds().SelectMany(x => (d.Summary(x)?.Findings ?? new()).Where(f => f.Actionable).Select(f => (x, f))).ToList();
        // CSS: page-level groups (the raw rows stay in runs/css_rows_all.md / .json only).
        var openCss = d.CssGroups().Where(g => g.Actionable).ToList();
        if (open.Count == 0 && openCss.Count == 0) sb.AppendLine("None.");
        if (open.Count > 0)
        {
            sb.AppendLine(Md.Header("Module", "ID", "Step", "Field", "Expected", "Actual", "Kind", "Details"));
            foreach (var (x, f) in open)
                sb.AppendLine(Md.Row(x, Md.Cell(f.Id, 500), Md.Cell(f.Step, 500), Md.Cell(f.Field, 500), Md.Cell(f.Expected, 500),
                    Md.Cell(f.Target, 500), f.Kind, Md.Cell(string.Join("; ", f.Details), 2000)));
            sb.AppendLine();
        }
        if (openCss.Count > 0)
        {
            sb.AppendLine(Md.Header("Issue", "Group", "Owner", "Elements", "States", "Property", "Reference", "Target", "Severity", "Seen in"));
            foreach (var g in openCss)
                sb.AppendLine(Md.Row(Md.Cell(ModuleReportGenerator.IssueOf(g), 2000), g.Id, g.Owner, Md.Cell(ModuleReportGenerator.GroupElements(g), 500), Md.Cell(string.Join(", ", g.States), 500),
                    Md.Cell(g.Property, 500), Md.Cell(g.Reference, 500), Md.Cell(g.Target, 500), g.Severity, string.Join(", ", g.Modules)));
            sb.AppendLine();
            sb.AppendLine("Every raw CSS row with its group: runs/css_rows_all.md (runs/css_rows_all.json).");
        }
        sb.AppendLine();

        // NOT_RUN
        sb.AppendLine("## NOT_RUN");
        sb.AppendLine();
        var notRun = NotRunModules(d);
        if (notRun.Count == 0) sb.AppendLine("None.");
        foreach (var (x, reason) in notRun)
            sb.AppendLine($"- Module {x}: {reason}; logs: logs/build-{x}.log, logs/test-{x}-Reference.log, logs/test-{x}-Target.log");
        return sb.ToString();
    }

    /// <summary>Drops the '# Module X Parity Report' title and demotes every other heading by one level.</summary>
    private static string DemoteParity(string parity)
    {
        var lines = parity.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[0].StartsWith("# ", StringComparison.Ordinal)) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].StartsWith("#", StringComparison.Ordinal)) lines[i] = "#" + lines[i];
        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    internal static List<(string Module, string Reason)> NotRunModules(ReportSources d)
    {
        var list = new List<(string, string)>();
        foreach (var x in d.PlannedIds().Concat(d.ModuleIds()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var he = d.HarnessError(x);
            if (he != null) list.Add((x, he));
            else if (d.Summary(x) == null) list.Add((x, "no runs/Module" + x + "/summary.json (module not run or reports not generated)"));
        }
        return list;
    }

    private static string ModuleResult(ReportSources d, string x)
    {
        var s = d.Summary(x);
        if (d.HarnessError(x) != null || s == null) return "NOT_RUN";
        return s.ReferenceStatus == "PASS" && s.TargetStatus == "PASS" ? "PASS" : "FAIL";
    }

    private static string Frac(RunResultsFile? r) => r == null ? "not run" : $"{r.Totals.StepsPass}/{r.Steps.Count}";

    /// <summary>
    /// Sections 1-7 of design §5.6 (each '###'). capped = chat view (cells capped, tables 25 rows);
    /// uncapped = FinalReport.md.
    /// </summary>
    internal static FinalSections BuildSections(ReportSources d, bool capped)
    {
        int Cap(int c) => capped ? c : 4000;
        // P7: plain-English field label; the audit md (uncapped) keeps the raw field name in a trailing "(field: ...)".
        string What(string field) => string.IsNullOrWhiteSpace(field) ? "" : capped ? d.Labels.Label(field) : $"{d.Labels.Label(field)} (field: {field})";
        // CSS property in plain English; the audit md keeps the raw property: "reading order (layout.order)".
        string Prop(string property) => capped ? FieldLabels.PropertyText(property) : FieldLabels.PropertyAudit(property);
        // Recorded values in plain English (sort states, booleans, placeholders); the audit md keeps the raw value in brackets.
        string Val(string field, string value)
        {
            var plain = FieldLabels.ValueText(field, value);
            return capped || plain == value ? plain : $"{plain} ({value})";
        }
        var report = "reports/FinalReport.md";
        var planned = d.PlannedIds();
        var all = planned.Concat(d.ModuleIds()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lt = d.Lt;

        var n = planned.Count;
        var refPass = planned.Count(x => d.Summary(x)?.ReferenceStatus == "PASS");
        var tgtPass = planned.Count(x => d.Summary(x)?.TargetStatus == "PASS");
        var openFindings = all.Sum(x => d.Summary(x)?.OpenFindings ?? 0);
        var actionableCss = all.Sum(x => d.Summary(x)?.ActionableCssDiffs ?? 0);
        var notRun = NotRunModules(d);
        var overall = n > 0 && refPass == n && tgtPass == n && openFindings == 0 && actionableCss == 0 && notRun.Count == 0 ? "PASS" : "FAIL";
        var unresolved = openFindings + actionableCss;

        var sb = new StringBuilder();

        // 1. Executive Summary
        sb.AppendLine("### Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"Overall: {overall}");
        sb.AppendLine();
        var rawCss = all.Sum(x => d.Summary(x)?.CssDiffs.Count(c => c.Actionable) ?? 0);
        sb.AppendLine($"Modules {n}; Reference PASS {refPass}; Target PASS {tgtPass}; open findings {openFindings}; actionable CSS groups {actionableCss} (raw rows {rawCss}); fixes applied {d.Fixes.Count}; unresolved {unresolved}.");
        sb.AppendLine();
        var oldPageDefects = all.Sum(x => d.Summary(x) is { } ms ? ModuleReportGenerator.ReferenceLargerGroups(ms) : 0);
        sb.AppendLine(Meaning(overall, n, tgtPass, openFindings, actionableCss, notRun.Count, oldPageDefects));
        sb.AppendLine();
        string elapsedText;
        if (lt.HasLifecycle)
        {
            elapsedText = LifecycleTimings.Mmss(lt.ElapsedMs);
            var within = lt.ElapsedMs <= lt.TotalBudgetMs ? "WITHIN TARGET" : "OVER TARGET";
            sb.AppendLine($"Total lifecycle time: {elapsedText} of {LifecycleTimings.Mmss(lt.TotalBudgetMs)} target (gate wait excluded: {LifecycleTimings.Mmss(lt.GateWaitMs)}) - {within}.");
        }
        else
        {
            elapsedText = "not measured";
            sb.AppendLine(LifecycleTimings.NoLifecycleTotal);
        }
        sb.AppendLine();
        sb.AppendLine("Time spent per lifecycle phase (mm:ss) against its time budget (the Target column here is the budget, not the new page).");
        sb.AppendLine();
        sb.AppendLine(Md.Header("Phase", "Target", "Measured", "Status"));
        for (var k = 0; k < LifecycleTimings.PhaseNames.Length; k++)
        {
            var label = $"Phase {k + 1} ({LifecycleTimings.PhaseNames[k]})";
            var budget = LifecycleTimings.Mmss(LifecycleTimings.PhaseBudgetMs[k]);
            if (!lt.HasLifecycle) { sb.AppendLine(Md.Row(label, budget, "not-measured", "not-measured")); continue; }
            var p = lt.Phases[k];
            var measured = p.Status == "not-measured" ? "not-measured" : LifecycleTimings.Mmss(p.ActiveMs);
            sb.AppendLine(Md.Row(label, LifecycleTimings.Mmss(p.BudgetMs), measured, p.Over ? p.Status + ", OVER" : p.Status));
        }
        if (lt.HasLifecycle)
        {
            sb.AppendLine(Md.Row("Orchestration (between phases)", "-", LifecycleTimings.Mmss(lt.OrchestrationMs), "-"));
            sb.AppendLine(Md.Row("Total", LifecycleTimings.Mmss(lt.TotalBudgetMs), LifecycleTimings.Mmss(lt.ElapsedMs),
                lt.ElapsedMs <= lt.TotalBudgetMs ? "WITHIN TARGET" : "OVER TARGET"));
        }
        else
        {
            sb.AppendLine(Md.Row("Orchestration (between phases)", "-", "not measured", "not-measured"));
            sb.AppendLine(Md.Row("Total", LifecycleTimings.Mmss(LifecycleTimings.DefaultTotalBudgetMs), "not measured", "not-measured"));
        }
        sb.AppendLine();

        // 2. Module Results
        sb.AppendLine("### Module Results");
        sb.AppendLine();
        sb.AppendLine("One row per module. Reference = old page, Target = new page (steps passed in brackets). The new page fails a module while any functional gap or styling/layout issue (CSS actionable) is open, even when every step passed.");
        var mrows = new List<string[]>();
        foreach (var x in all)
        {
            var s = d.Summary(x);
            var r = d.Results(x, "Reference");
            var t = d.Results(x, "Target");
            mrows.Add(new[]
            {
                x, Md.Cell(d.ModuleName(x), Cap(Md.DescriptionCap)), d.DataEffectLabel(x),
                s == null ? "NOT_RUN" : $"{s.ReferenceStatus} ({Frac(r)})",
                s == null ? "NOT_RUN" : $"{s.TargetStatus} ({Frac(t)})",
                s?.OpenFindings.ToString() ?? "-", s?.ActionableCssDiffs.ToString() ?? "-",
                d.Fixes.Count(f => f.Module.Equals(x, StringComparison.OrdinalIgnoreCase)).ToString(),
                ModuleResult(d, x)
            });
        }
        Md.Table(sb, new[] { "Module", "Workflow", "Data effect", "Reference", "Target", "Open findings", "CSS actionable", "Fixes", "Result" }, mrows, capped, report);
        sb.AppendLine();

        // 3. Functional Gaps
        sb.AppendLine("### Functional Gaps");
        sb.AppendLine();
        sb.AppendLine("Behavior or data that differs between the old page (Reference) and the new page (Target), open ones first, then those resolved in fix rounds (\"-\" = value not recorded for a resolved gap).");
        var grows = new List<string[]>();
        var openIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in all)
            foreach (var f in (d.Summary(x)?.Findings ?? new()).OrderBy(f => f.Actionable ? 0 : 1).ThenBy(f => Md.SevRank(f.Severity)))
            {
                openIds.Add(f.Id);
                grows.Add(new[]
                {
                    x, Md.Cell(f.Id, Cap(Md.ValueCap)), f.Actionable ? f.Severity : f.Severity + " (info)", Md.Cell(f.Step, Cap(Md.ValueCap)),
                    Md.Cell(What(f.Field), Cap(Md.ValueCap)), Md.Cell(Val(f.Field, f.Reference), Cap(Md.ValueCap)), Md.Cell(Val(f.Field, f.Target), Cap(Md.ValueCap)),
                    f.Actionable ? "UNRESOLVED" : "INFO (not counted)"
                });
            }
        foreach (var fx in d.Fixes.OrderBy(f => f.Round))
            foreach (var id in fx.FindingIds.Where(i => !i.StartsWith("css:", StringComparison.Ordinal) && !openIds.Contains(i)))
            {
                if (grows.Any(g => g[1] == Md.Cell(id, Cap(Md.ValueCap)) && g[7].StartsWith("RESOLVED"))) continue;
                // One fix can resolve findings of several modules: the module comes from the finding ID prefix ("B-Step0-...").
                var parts = id.Split('-', 3);
                var gm = parts.Length == 3 && all.Contains(parts[0], StringComparer.OrdinalIgnoreCase) ? parts[0].ToUpperInvariant() : fx.Module;
                // Values as they were before the fix (summary.before-round{N}.json); Target = the value before the fix.
                var prior = d.PriorFinding(gm, fx.Round, id) ?? d.PriorFinding(fx.Module, fx.Round, id);
                grows.Add(prior != null
                    ? new[]
                    {
                        gm, Md.Cell(id, Cap(Md.ValueCap)), prior.Severity, Md.Cell(prior.Step, Cap(Md.ValueCap)), Md.Cell(What(prior.Field), Cap(Md.ValueCap)),
                        Md.Cell(Val(prior.Field, prior.Reference), Cap(Md.ValueCap)), Md.Cell(Val(prior.Field, prior.Target), Cap(Md.ValueCap)), $"RESOLVED (round {fx.Round})"
                    }
                    : new[]
                    {
                        gm, Md.Cell(id, Cap(Md.ValueCap)), "-", parts.Length == 3 ? Md.Cell(parts[1], Cap(Md.ValueCap)) : "-",
                        parts.Length == 3 ? Md.Cell(What(parts[2]), Cap(Md.ValueCap)) : "-", "-", "-", $"RESOLVED (round {fx.Round})"
                    });
            }
        Md.Table(sb, new[] { "Module", "ID", "Severity", "Step", "What", "Reference", "Target", "Status" }, grows, capped, report);
        sb.AppendLine();

        // 4. CSS Comparison
        sb.AppendLine("### CSS Comparison");
        sb.AppendLine();
        sb.AppendLine("Styling and layout differences of the new page (Target) against the old page (Reference), one row per fix. The Issue column says what differs in plain English; the other columns are the technical detail" +
                      (capped ? "." : "; accepted differences follow the open ones."));
        // Page-level groups (one row per theme rule / element finding). Chat: top ChatTop actionable groups + "N more".
        var groups = d.CssGroups();
        var actionableGroups = groups.Where(g => g.Actionable).ToList(); // already sorted: severity, element count desc
        var shownGroups = capped ? actionableGroups.Take(d.ChatTopGroups).ToList() : actionableGroups;
        var crows = new List<string[]>();
        string[] GroupRow(CssGroup g, string action) => new[]
        {
            Md.Cell(ModuleReportGenerator.IssueOf(g), Cap(Md.IssueCap)),
            g.Id, Md.Cell(ModuleReportGenerator.GroupElements(g), Cap(Md.ValueCap)), Md.Cell(Prop(g.Property), Cap(Md.ValueCap)),
            Md.Cell(g.Reference, Cap(Md.ValueCap)), Md.Cell(g.Target, Cap(Md.ValueCap)), g.Severity,
            Md.Cell(ModuleReportGenerator.GroupSeenIn(g), Cap(Md.ValueCap)), Md.Cell(action, Cap(Md.DescriptionCap))
        };
        foreach (var g in shownGroups) crows.Add(GroupRow(g, $"UNRESOLVED (Module {g.Owner})"));
        var acceptedGroups = groups.Where(g => g.Accepted).ToList();
        if (!capped)
            foreach (var g in acceptedGroups)
                crows.Add(GroupRow(g, g.AcceptedBy?.StartsWith("profile ", StringComparison.Ordinal) == true ? g.AcceptedNote ?? "" : "accepted: " + g.AcceptedNote));
        Md.Table(sb, new[] { "Issue", "Group", "Elements", "Property", "Reference", "Target", "Severity", "Seen in", "Action" }, crows, capped, report);
        if (actionableGroups.Count > shownGroups.Count)
            sb.AppendLine($"{actionableGroups.Count - shownGroups.Count} more in reports/CSS_Groups.md");
        sb.AppendLine();
        var refWorse = groups.Count(g => g.ReferenceWorse && !g.Accepted);
        sb.AppendLine($"CSS groups: {actionableGroups.Count} actionable, {acceptedGroups.Count} accepted, {groups.Count} total (raw rows: {rawCss} actionable; audit runs/css_rows_all.md)." +
                      (refWorse > 0 ? $" {refWorse} more are old-page-only problems at other window widths (Reference worse, not counted; reports/CSS_Groups.md)." : ""));
        sb.AppendLine();
        var profiles = all.Select(x => d.Summary(x)?.StackProfile).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        if (profiles.Count > 0)
        {
            var byProfile = all.Sum(x => (d.Summary(x)?.CssDiffs ?? new()).Count(c => c.AcceptedBy?.StartsWith("profile ", StringComparison.Ordinal) == true));
            sb.AppendLine($"Stack profile: {string.Join(", ", profiles)}, {byProfile} rows accepted by profile");
        }
        var resolvedCss = d.Fixes.SelectMany(f => f.FindingIds.Where(i => i.StartsWith("css:", StringComparison.Ordinal)).Select(i => (f.Module, Key: i)))
            .Distinct().Count(k => !CssStillActionable(d, k.Module, k.Key));
        sb.AppendLine($"Resolved in fix rounds: {resolvedCss}");
        var uncompared = all.Select(x => (x, States: d.UncomparedCssStates(x))).Where(u => u.States.Count > 0).ToList();
        if (uncompared.Count > 0)
            sb.AppendLine("Not compared (no state-map/css-map.json elements for these states): " +
                          string.Join("; ", uncompared.Select(u => $"Module {u.x}: {string.Join(", ", u.States)}")) + ".");
        sb.AppendLine();

        // 5. Bug Fixes
        sb.AppendLine("### Bug Fixes");
        sb.AppendLine();
        sb.AppendLine("Every edit made by the fix rounds, in order. Kind: app = new page (Target) code, test-* = test code; Before -> After shows the first changed line.");
        var frows = d.Fixes.OrderBy(f => f.Round).Select(f => new[]
        {
            f.Round.ToString(), f.Module, Md.Cell(f.Kind), Md.Cell(f.File, Cap(Md.DescriptionCap)),
            $"`{Md.Cell(f.Before, Cap(Md.CodeCap))}` -> `{Md.Cell(f.After, Cap(Md.CodeCap))}`"
        }).ToList();
        Md.Table(sb, new[] { "Round", "Module", "Kind", "File", "Before -> After" }, frows, capped, report);
        sb.AppendLine();

        // 6. Manual Tests
        sb.AppendLine("### Manual Tests");
        sb.AppendLine();
        sb.AppendLine("Items the automation did not check (NOT_TESTED); a person verifies them by hand.");
        var trows = new List<string[]>();
        foreach (var x in all)
        {
            foreach (var p in d.PlanModule(x)?.NotTested ?? new List<PlanNotTested>())
                trows.Add(new[] { x, Md.Cell(p.Item, Cap(Md.DescriptionCap)), Md.Cell(p.Reason, Cap(Md.DescriptionCap)) });
            foreach (var sys in new[] { "Reference", "Target" })
                foreach (var st in (d.Results(x, sys)?.Steps ?? new()).Where(s => s.Status == "NOT TESTED"))
                {
                    var parts = st.Description.Split(" | ", 2);
                    trows.Add(new[] { x, Md.Cell($"{sys} {st.Step}: {parts[0]}", Cap(Md.DescriptionCap)), Md.Cell(parts.Length > 1 ? parts[1] : "", Cap(Md.DescriptionCap)) });
                }
        }
        Md.Table(sb, new[] { "Module", "Item", "Reason" }, trows, capped, report);
        sb.AppendLine();

        // 7. Limitations
        sb.AppendLine("### Limitations");
        sb.AppendLine();
        var lim = new List<string>();
        foreach (var (x, reason) in notRun)
            lim.Add($"Module {x} NOT_RUN: {reason}; logs: logs/build-{x}.log, logs/test-{x}-*.log");
        if (lt.HasLifecycle)
        {
            foreach (var p in lt.Phases.Where(p => p.Over))
                lim.Add($"Phase {p.Id + 1} ({p.Name}) over budget: {LifecycleTimings.Mmss(p.ActiveMs)} of {LifecycleTimings.Mmss(p.BudgetMs)}");
            if (lt.PostGateMs > 0) lim.Add($"post-gate time (after the final report, not counted): {LifecycleTimings.Mmss(lt.PostGateMs)}");
        }
        else lim.Add("phase timing not measured (no lifecycle recorded in timings.json)");
        if (d.FixRound >= d.MaxRounds && d.FixRound > 0) lim.Add($"fix budget used ({d.FixRound} of {d.MaxRounds} rounds)");
        var refDefects = all.SelectMany(x => (d.Summary(x)?.ReferenceDefects ?? new()).Select(r => (x, r))).ToList();
        if (refDefects.Count > 0)
            lim.Add($"{refDefects.Count} Reference defect(s) found on the baseline (see Reference Issues); they do not fail Target parity");
        foreach (var l in lim) sb.AppendLine("- " + l);
        if (lim.Count == 0) sb.AppendLine("- none");
        sb.AppendLine();

        // 8. Reference Issues: baseline problems found by the content checks (never counted in Target parity).
        sb.AppendLine("### Reference Issues");
        sb.AppendLine();
        sb.AppendLine("Problems found on the old page (Reference) itself, for example server code shown as page text. They are recorded for the record and never counted against or copied to the new page.");
        var rrows = refDefects.Select(p => new[]
        {
            p.x, Md.Cell(p.r.Id, Cap(Md.ValueCap)), p.r.Severity, Md.Cell(string.Join(", ", p.r.Steps.DefaultIfEmpty(p.r.Step)), Cap(Md.ValueCap)),
            Md.Cell(ModuleReportGenerator.ContentKindText(p.r.Kind), Cap(Md.ValueCap)), Md.Cell(ModuleReportGenerator.ReferenceDefectText(p.r), Cap(Md.DescriptionCap))
        }).ToList();
        Md.Table(sb, new[] { "Module", "ID", "Severity", "Step", "Kind", "Text" }, rrows, capped, report);

        var summary = $"Final: {overall}; {refPass}/{n} Ref PASS, {tgtPass}/{n} Target PASS, {openFindings} open findings; total {elapsedText} of {LifecycleTimings.Mmss(lt.TotalBudgetMs)}.";
        return new FinalSections { Markdown = sb.ToString(), Overall = overall, Summary = summary };
    }

    /// <summary>"What this means / what to do" line of the Executive Summary (deterministic from the counts).</summary>
    internal static string Meaning(string overall, int modules, int targetPass, int openFindings, int actionableCss, int notRun, int oldPageDefects = 0)
    {
        if (overall == "PASS")
            return "What this means: the new page (Target) matches the old page (Reference) in every module. What to do: review the Manual Tests below, then sign off.";
        var open = new List<string>();
        if (notRun > 0) open.Add($"{notRun} module{(notRun == 1 ? "" : "s")} did not run");
        if (openFindings > 0) open.Add($"{openFindings} functional gap{(openFindings == 1 ? "" : "s")}");
        if (actionableCss > 0) open.Add($"{actionableCss} styling/layout issue{(actionableCss == 1 ? "" : "s")}" + ChatRenderer.OldPageDefects(oldPageDefects, actionableCss));
        if (open.Count == 0) open.Add($"{modules - targetPass} module{(modules - targetPass == 1 ? "" : "s")} did not meet every pass rule");
        var list = open.Count == 1 ? open[0] : string.Join(", ", open.Take(open.Count - 1)) + " and " + open[^1];
        var todo = oldPageDefects > 0 && actionableCss > 0
            ? "What to do: fix the other issues in the new page (CSS Comparison and Functional Gaps list each one) or accept an intended difference in state-map/css-map.json; " +
              "confirm the old-page defects on the old page and accept them in state-map/css-map.json (never copy them into the new page); then re-run."
            : "What to do: fix them in the new page (CSS Comparison and Functional Gaps list each one), or accept an intended difference in state-map/css-map.json, then re-run.";
        return $"What this means: the new page (Target) is not at parity yet: {list} still open. " + todo;
    }

    public const string CssGroupPrefix = "css:group:";

    /// <summary>
    /// A fixer CSS key is still open when: "css:group:{key}" - an actionable page-level group has that key, or any
    /// module still has an actionable row of it; "css:{element}.{property}" - an actionable diff of the module has that
    /// element and property.
    /// </summary>
    internal static bool CssStillActionable(ReportSources d, string module, string key)
    {
        if (key.StartsWith(CssGroupPrefix, StringComparison.Ordinal))
        {
            var gk = key[CssGroupPrefix.Length..];
            return d.CssGroups().Any(g => g.Actionable && g.Key == gk)
                   || d.ModuleIds().Any(x => d.Summary(x)?.CssDiffs.Any(c => c.Actionable && c.GroupKey == gk) == true);
        }
        return CssStillActionable(d.Summary(module), key);
    }

    /// <summary>A fixer CSS key "css:{element}.{property}" (or "css:group:{key}") is still open in the module's rows.</summary>
    internal static bool CssStillActionable(ModuleSummary? s, string key)
    {
        if (s == null) return true;
        if (key.StartsWith(CssGroupPrefix, StringComparison.Ordinal))
            return s.CssDiffs.Any(c => c.Actionable && c.GroupKey == key[CssGroupPrefix.Length..]);
        var k = key["css:".Length..];
        return s.CssDiffs.Any(c => c.Actionable && (k == $"{c.Element}.{c.Property}" || k == $"{c.Element} [{c.State}].{c.Property}"));
    }
}

using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>
/// Browser-free tests for ChatRenderer (design §5) and LifecycleTimings (design §4) against
/// Fixtures/PayorListMini (copied to a temp dir per test because the final view rewrites FinalReport.md).
/// </summary>
[TestFixture]
public class ChatRendererTests
{
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-27T10:20:00.0000000+05:30");
    private const string Progress = "Phase 5 of 5 (Report) - elapsed 11:30 - estimated remaining 00:00 - waiting for you";
    private string _pageDir = "";

    [SetUp]
    public void SetUp() => _pageDir = CopyFixture();

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("LIFECYCLE_RUN_WALL_MS", null);
        try { if (Directory.Exists(_pageDir)) Directory.Delete(Path.GetDirectoryName(_pageDir)!, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    internal static string CopyFixture()
    {
        var src = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "PayorListMini");
        var dst = Path.Combine(Path.GetTempPath(), "chat-tests-" + Guid.NewGuid().ToString("N"), "PayorList");
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target);
        }
        return dst;
    }

    /// <summary>Every part of the view joined in relay order (only the plan has more than one part).</summary>
    private (string Markdown, string Summary) Render(string view, string? module = null, string? progress = Progress)
    {
        var (parts, summary) = ChatRenderer.RenderParts(_pageDir, view, module, progress, Now);
        return (string.Join(Environment.NewLine, parts.Select(p => p.Markdown)), summary);
    }

    private static string[] Lines(string md) => md.Replace("\r\n", "\n").Split('\n');

    /// <summary>Adds an automatic layout row and a profile-accepted row to Module A's summary.</summary>
    internal static void AddAutoRows(string pageDir)
    {
        var path = Path.Combine(pageDir, "runs/ModuleA/summary.json");
        var s = JsonNode.Parse(File.ReadAllText(path))!;
        s["stackProfile"] = "webforms-jqgrid__blazor-mudblazor";
        var css = s["cssDiffs"]!.AsArray();
        css.Add(new JsonObject
        {
            ["element"] = "button 'Search'", ["state"] = "page_load", ["property"] = "layout.order", ["reference"] = "position 1 of 2",
            ["target"] = "position 2 of 2", ["severity"] = "HIGH", ["actionable"] = true, ["layer"] = "layout"
        });
        css.Add(new JsonObject
        {
            ["element"] = "button 'Save'", ["state"] = "page_load", ["property"] = "disabled.cursor", ["reference"] = "default",
            ["target"] = "not-allowed", ["severity"] = "MEDIUM", ["accepted"] = true, ["acceptedReason"] = "MudBlazor default",
            ["acceptedBy"] = "profile webforms-jqgrid__blazor-mudblazor", ["layer"] = "state"
        });
        s["actionableCssDiffs"] = s["actionableCssDiffs"]!.GetValue<int>() + 1;
        s["cssDiffsTotal"] = css.Count;
        File.WriteAllText(path, s.ToJsonString());
    }

    [Test]
    public void ModuleResult_AutoLayerRow_PlainEnglish_AndProfileLine()
    {
        AddAutoRows(_pageDir);
        var (md, _) = Render("module-result", "A");
        Assert.That(md, Does.Contain("| G1 | 1: button 'Search' | reading order | position 1 of 2 | position 2 of 2 | HIGH | A [page_load] |"));
        Assert.That(md, Does.Contain("Stack profile: webforms-jqgrid__blazor-mudblazor, 1 rows accepted by profile"));
    }

    private JsonNode ReadJson(string rel) => JsonNode.Parse(File.ReadAllText(Path.Combine(_pageDir, rel)))!;
    private void WriteJson(string rel, JsonNode node) => File.WriteAllText(Path.Combine(_pageDir, rel), node.ToJsonString());

    // ───────────────────────────── common rules ─────────────────────────────

    [TestCase("plan", null, "### Test plan")]
    [TestCase("tests-authored", null, "### Test cases written and compiled (not run yet)")]
    [TestCase("live-status", null, "### Run status")]
    [TestCase("module-result", "A", "### Module A: Load the page and sort the grid")]
    [TestCase("run-summary", null, "### Run summary")]
    [TestCase("fix-round", null, "### Fix round 1")]
    [TestCase("final", null, "### Final report")]
    public void EveryView_ThreeLineHeader_TitlePagePhaseAndRunTiming(string view, string? module, string title)
    {
        var lines = Lines(Render(view, module).Markdown);
        Assert.That(lines[0], Is.EqualTo(title));
        Assert.That(lines[1], Is.EqualTo(""), "line 2 is its own paragraph");
        Assert.That(lines[2], Is.EqualTo("PayorList · Phase 5 of 5: Report"));
        Assert.That(lines[3], Is.EqualTo(""), "line 3 is its own paragraph");
        Assert.That(lines[4], Is.EqualTo("Run id not recorded · elapsed 11:30 · waiting for you"));
        Assert.That(lines[5], Is.EqualTo(""), "blank line after the header");
    }

    [TestCase("plan", null, "### Test plan")]
    [TestCase("module-result", "A", "### Module A: Load the page and sort the grid")]
    [TestCase("final", null, "### Final report")]
    public void EveryView_WithSession_HeaderNamesTheRun(string view, string? module, string title)
    {
        var st = ReadJson("status.json");
        st["session"] = new JsonObject { ["id"] = "run-20261028-101500", ["startedAt"] = "2026-10-28T10:15:00.0000000+05:30", ["planRevision"] = 2 };
        WriteJson("status.json", st);
        var lines = Lines(Render(view, module).Markdown);
        Assert.That(lines[0], Is.EqualTo(title));
        Assert.That(lines[4], Is.EqualTo("Run `run-20261028-101500` · started 28 Oct 10:15 · elapsed 11:30 · waiting for you"));
        if (view == "final")
        {
            var report = File.ReadAllText(Path.Combine(_pageDir, "reports", "FinalReport.md"));
            Assert.That(report, Does.StartWith("# Final Report - PayorList - Run run-20261028-101500 (started 28 Oct 10:15)"));
            Assert.That(report, Does.Contain("Run id: run-20261028-101500 - started 2026-10-28 10:15:00 - plan revision 2"));
        }
    }

    [Test]
    public void ProgressLine_WhenNotPassed_IsComputedFromTimings()
    {
        var lines = Lines(Render("plan", progress: null).Markdown);
        Assert.That(lines[2], Is.EqualTo("PayorList · Phase 5 of 5: Report"));
        Assert.That(lines[4], Is.EqualTo("Run id not recorded · elapsed 11:30 · waiting for you"));
    }

    [Test]
    public void Header_RunningShowsTimeLeft_FinishedShowsFinished_UnmeasuredKeepsText()
    {
        const string running = "Phase 3 of 5 (Run Reference + Target) - elapsed 01:17 - estimated remaining 01:06";
        // Fixture lifecycle is finished: no time left is shown.
        Assert.That(Lines(Render("live-status", progress: running).Markdown)[4], Is.EqualTo("Run id not recorded · elapsed 01:17 · finished"));
        var tm = ReadJson("timings.json");
        tm["lifecycles"]![tm["lifecycles"]!.AsArray().Count - 1]!["finishedAt"] = null;
        WriteJson("timings.json", tm);
        var lines = Lines(Render("live-status", progress: running).Markdown);
        Assert.That(lines[0], Is.EqualTo("### Run status"));
        Assert.That(lines[2], Is.EqualTo("PayorList · Phase 3 of 5: Run Reference + Target"));
        Assert.That(lines[4], Is.EqualTo("Run id not recorded · elapsed 01:17 · about 01:06 left"));
        var none = Lines(Render("live-status", progress: LifecycleTimings.NoLifecycleLine).Markdown);
        Assert.That(none[2], Is.EqualTo("PayorList"));
        Assert.That(none[4], Is.EqualTo("Run id not recorded · phase timing not measured (no lifecycle recorded)"));
    }

    [Test]
    public void MissingPlan_ThrowsReportInputException()
    {
        File.Delete(Path.Combine(_pageDir, "state-map", "plan.json"));
        Assert.Throws<ReportInputException>(() => Render("plan"));
    }

    [Test]
    public void ModuleResult_MissingRunsFolder_ThrowsReportInputException_AndMissingModuleArgThrowsArgument()
    {
        Assert.Throws<ReportInputException>(() => Render("module-result", "Z"));
        Assert.Throws<ArgumentException>(() => Render("module-result", null));
        Assert.Throws<ArgumentException>(() => Render("nonsense"));
    }

    [Test]
    public void CellRules_PipeIsEscaped_NewlinesBecomeSpace_CapWithEllipsis()
    {
        Assert.That(Md.Cell("a|b\r\nc"), Is.EqualTo("a\\|b c"));
        Assert.That(Md.SplitRow("| " + Md.Cell("a|b\r\nc") + " | x |"), Is.EqualTo(new[] { "a|b c", "x" }));
        // A cap cuts at the last sentence end in the second half of the cell, else at a word boundary; never '...'.
        Assert.That(Md.Cell("First sentence is here. Second sentence runs on and on", 40), Is.EqualTo("First sentence is here."));
        Assert.That(Md.Cell("alpha beta gamma delta epsilon zeta eta theta", 28), Is.EqualTo("alpha beta gamma delta"));
        var longText = new string('x', 300);
        Assert.That(Md.Cell(longText, Md.DescriptionCap), Is.EqualTo(new string('x', 200)));
        Assert.That(Md.Cell(longText), Is.EqualTo(new string('x', 80)));
        Assert.That(Md.Cell(longText, Md.CodeCap), Is.EqualTo(new string('x', 60)));
        var words = string.Join(" ", Enumerable.Range(1, 80).Select(i => "word" + i));
        var cut = Md.Cell(words, Md.DescriptionCap);
        Assert.That(cut.Length, Is.LessThanOrEqualTo(200));
        Assert.That(words, Does.StartWith(cut + " "), "cut at a word boundary");
        Assert.That(cut, Does.Not.EndWith("..."));
    }

    // ─────────────────────────────── plan ───────────────────────────────

    [Test]
    public void Plan_HeadersRealValuesWarningAndSummary()
    {
        // Overview part only (suffix ""), not the per-module step parts.
        var (parts, summary) = ChatRenderer.RenderParts(_pageDir, "plan", null, Progress, Now);
        var md = parts.Single(p => p.Suffix.Length == 0).Markdown;
        Assert.That(md, Does.Contain("Plan revision 2."));
        Assert.That(md, Does.Contain("| Module | Workflow (user story) | Steps | Planned checks | Data effect | CSS states |"));
        Assert.That(md, Does.Contain("What each module tests:"), "purpose line above the table");
        Assert.That(md, Does.Contain("| A | Load the page and sort the grid: As a user I open Payor List, check the first page and sort by Name | 2 | 4 | read-only | page_load, sorted |"));
        Assert.That(md, Does.Contain("| B | Edit a cell and Save All | 3 | 4 | mutating (default: not set in plan) | (none) |"), "missing dataEffect defaults to mutating");
        Assert.That(md, Does.Contain("WARNING: Module B plannedSteps 3 but 2 steps listed in plan.json"));
        Assert.That(md, Does.Not.Contain("WARNING: Module A"));
        Assert.That(md, Does.Not.Contain("| Step1 |"), "steps are in the per-module parts");
        Assert.That(md, Does.Contain("The steps of each module follow in separate messages: Module A, Module B."));
        Assert.That(md, Does.Contain("| State | How reached | Key indicators |"));
        Assert.That(md, Does.Contain("pager \"View 1 - 10 of 15\""));
        Assert.That(md, Does.Contain("| Item | Reason |"));
        Assert.That(md, Does.Contain("| A: Sort-indicator glyph visibility |"));
        Assert.That(summary, Is.EqualTo("Plan revision 2: 2 modules (A read-only; B mutating), 5 steps, 8 planned checks."));
    }

    [Test]
    public void Plan_NoStatesAndNoNotTested_RenderNoneMarkers()
    {
        var plan = ReadJson("state-map/plan.json");
        plan.AsObject().Remove("states");
        foreach (var m in plan["modules"]!.AsArray()) m!["notTested"] = new JsonArray();
        WriteJson("state-map/plan.json", plan);
        var md = Render("plan").Markdown;
        Assert.That(md, Does.Contain("(no states listed in plan.json)"));
        var notTested = md[md.IndexOf("### NOT_TESTED", StringComparison.Ordinal)..];
        Assert.That(notTested, Does.Contain("| None found. |  |"));
    }

    [Test]
    public void Plan_SplitsIntoOverviewAndOnePartPerModule()
    {
        var (parts, summary) = ChatRenderer.RenderParts(_pageDir, "plan", null, Progress, Now);
        Assert.That(parts.Select(p => p.Suffix), Is.EqualTo(new[] { "", "A", "B" }));
        var a = parts[1].Markdown;
        Assert.That(Lines(a)[0], Is.EqualTo("### Test plan - Module A: Load the page and sort the grid"));
        Assert.That(Lines(a)[2], Is.EqualTo("PayorList · Phase 5 of 5: Report"));
        Assert.That(a, Does.Contain("2 steps, read-only."));
        Assert.That(a, Does.Contain("| Step | What the user does | What is checked |"));
        // P3: checksText only, no observed values.
        Assert.That(a, Does.Contain("| Step1 | Open /Pages/PayorList and read the first page | The pager range text ('View x - y of z') shows the first page. The grid shows one full page of rows. |"));
        Assert.That(a, Does.Contain("| Step2 | Click the Name header | The first row becomes the first name in A to Z order. The pager range text stays the same. |"));
        // Without checksText: field labels only (fieldLabels map), never values.
        var b = parts[2].Markdown;
        Assert.That(b, Does.Contain("| Step1 | Edit Payor Name of row 1 to 'Acme Plus' | Checks: Payor Name in grid row 1 changes; Pager text stays the same. |"));
        Assert.That(b, Does.Contain("| Step2 | Click Save All | Checks: Message changes; Payor Name in grid row 1 is checked. |"));
        Assert.That(ChatRenderer.Render(_pageDir, "plan", null, Progress, Now).Markdown, Is.EqualTo(parts[0].Markdown));
        Assert.That(summary, Does.StartWith("Plan revision 2:"));
    }

    [Test]
    public void Plan_ChecksFallback_UsesFieldOnly_NotValues()
    {
        var step = new PlanStep { Changes = { "pager.info 'View 1 - 10 of 15' -> 'View 11 - 15 of 15'", "grid.row[0].id 1 -> 11" }, StaysSame = { "pager.total 15" } };
        var text = ChatRenderer.CheckText(step, new FieldLabels(new Dictionary<string, string> { ["pager.info"] = "Pager text", ["grid.row[0].id"] = "First row's ID" }));
        Assert.That(text, Is.EqualTo("Checks: Pager text and First row's ID change; Pager total stays the same."));
        Assert.That(text, Does.Not.Contain("15"));
    }

    // ─────────────────────────── tests-authored ──────────────────────────

    [Test]
    public void TestsAuthored_HasNoPassWordAndExplainsPhase()
    {
        var (md, summary) = Render("tests-authored");
        Assert.That(md, Does.Contain(ChatRenderer.TestsAuthoredExplainer));
        Assert.That(md, Does.Contain("| Module | Workflow | Steps (Reference / Target / planned) | Checks in code (Reference / Target) | Status | Time |"));
        Assert.That(md, Does.Contain("| A | Load the page and sort the grid | 2 / 2 / 2 | 4 / 4 | Written + compiled | 02:05 |"));
        Assert.That(md, Does.Contain("| B | Edit a cell and Save All | 2 / 2 / 3 | 3 / 3 | Unchanged from last run, compiled | reused |"));
        Assert.That(md, Does.Contain("B: 2 steps vs planned 3"));
        Assert.That(md, Does.Not.Contain("PASS"));
        Assert.That(md, Does.Not.Contain("authored in parallel"));
        Assert.That(summary, Is.EqualTo("2 of 2 modules ready to run (1 written now, 1 reused from the previous run)."));
        Assert.That(summary, Does.Not.Contain("PASS"));
    }

    [Test]
    public void TestsAuthored_StepNames_ExplainExtraNavStep()
    {
        var b = ReadJson("runs/ModuleB/build.json");
        b["reference"]!["steps"] = 3;
        b["reference"]!["stepNames"] = new JsonArray("NAV", "Step1", "Step2");
        WriteJson("runs/ModuleB/build.json", b);
        var md = Render("tests-authored").Markdown;
        Assert.That(md, Does.Contain("- B: 3 steps vs planned 3: includes NAV (page-open check), not a planned step."));
    }

    [Test]
    public void TestsAuthored_MissingBuildAndFailedBuild()
    {
        File.Delete(Path.Combine(_pageDir, "runs", "ModuleB", "build.json"));
        var b = ReadJson("runs/ModuleA/build.json");
        b["result"] = "FAIL";
        b["errors"] = new JsonArray("PayorListModuleA_v2.cs(42,13): error CS1002: ; expected");
        b["target"]!["methodFound"] = false;
        WriteJson("runs/ModuleA/build.json", b);
        var (md, summary) = Render("tests-authored");
        Assert.That(md, Does.Contain("| B | Edit a cell and Save All | - / - / 3 | - / - | Not compiled yet | - |"));
        Assert.That(md, Does.Contain("Compile failed: PayorListModuleA_v2.cs(42,13): error CS1002: ; expected (Target test method PayorList_ModA not found)"));
        Assert.That(md, Does.Not.Contain("PASS"));
        Assert.That(summary, Is.EqualTo("0 of 2 modules ready to run (1 written now); not ready: A (compile failed), B (not compiled yet)."));
    }

    // ───────────────────────────── live-status ─────────────────────────────

    [Test]
    public void LiveStatus_ListsModulesInPlanOrder()
    {
        // B finished first (out of plan order) while A Target is still running.
        var st = ReadJson("status.json");
        st["currentRun"]!["jobs"] = new JsonArray(new JsonObject { ["module"] = "A", ["system"] = "Target", ["step"] = "Step1", ["stepIndex"] = 2, ["stepTotal"] = 3 });
        WriteJson("status.json", st);
        var tm = ReadJson("timings.json");
        tm["runs"]![0]!["modules"] = new JsonArray("B", "A");
        WriteJson("timings.json", tm);
        var (md, summary) = Render("live-status");
        var rows = Lines(md).Where(l => l.StartsWith("| A |") || l.StartsWith("| B |")).ToList();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0], Does.StartWith("| A |"), "plan order, not finish order");
        Assert.That(md, Does.Contain("| Module | Old page (Reference) | New page (Target) | Status |"));
        Assert.That(md, Does.Contain("Progress of each module on the old page (Reference) and the new page (Target)"), "purpose line above the table");
        Assert.That(rows[0], Is.EqualTo("| A | 3/3 PASS | running (step 2 of 3) | running |"));
        Assert.That(rows[1], Is.EqualTo("| B | 2/2 PASS | 2/2 PASS | done: 0 open functional gaps, 0 CSS issues |"));
        Assert.That(summary, Is.EqualTo("Run progress: 1 of 2 modules finished (A running, B done)."));
    }

    // ───────────────────────────── table shape ─────────────────────────────

    [Test]
    public void EveryView_TableRowsHaveHeaderColumnCount()
    {
        // Fixture cells with '|' and a newline must not add columns.
        var plan = ReadJson("state-map/plan.json");
        plan["modules"]![0]!["steps"]![0]!["action"] = "Open the page | read\nthe first page";
        plan["modules"]![0]!["notTested"]![0]!["reason"] = "a|b\r\nc";
        WriteJson("state-map/plan.json", plan);
        var r = ReadJson("runs/ModuleA/Target/results.json");
        r["steps"]![1]!["title"] = "Open | read\nfirst page";
        WriteJson("runs/ModuleA/Target/results.json", r);

        var rendered = new List<(string Name, string Md)>();
        foreach (var view in ChatRenderer.Views)
        {
            var module = view == "module-result" ? "A" : null;
            var (parts, _) = ChatRenderer.RenderParts(_pageDir, view, module, Progress, Now);
            rendered.AddRange(parts.Select(p => ($"{view}{(p.Suffix.Length > 0 ? "-" + p.Suffix : "")}", p.Markdown)));
        }
        rendered.Add(("FinalReport.md", File.ReadAllText(Path.Combine(_pageDir, "reports", "FinalReport.md"))));
        foreach (var (name, md) in rendered)
        {
            var lines = Lines(md);
            var tables = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith('|') || (i > 0 && lines[i - 1].StartsWith('|'))) continue;
                tables++;
                Assert.That(i == 0 || lines[i - 1].Length == 0, $"{name}: blank line before the table at line {i + 1}");
                var cols = Md.SplitRow(lines[i]).Count;
                var j = i + 1;
                for (; j < lines.Length && lines[j].StartsWith('|'); j++)
                    Assert.That(Md.SplitRow(lines[j]).Count, Is.EqualTo(cols), $"{name} line {j + 1}: {lines[j]}");
                Assert.That(j == lines.Length || lines[j].Length == 0, $"{name}: blank line after the table ending at line {j}");
            }
            Assert.That(tables, Is.GreaterThan(0), name);
        }
    }

    [Test]
    public void Table_HasBlankLineBeforeAndAfter_AndOverflowRowIsPadded()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("text");
        var rows = Enumerable.Range(1, 30).Select(i => new[] { $"S{i}", "PASS", "d" }).ToList();
        Md.Table(sb, new[] { "Step", "Status", "Description" }, rows, true, "reports/X.md");
        sb.Append("after");
        var lines = Lines(sb.ToString());
        Assert.That(lines[1], Is.EqualTo(""));
        Assert.That(lines[2], Is.EqualTo("| Step | Status | Description |"));
        var overflow = lines.First(l => l.StartsWith("| ... |"));
        Assert.That(Md.SplitRow(overflow), Has.Count.EqualTo(3));
        Assert.That(lines[^2], Is.EqualTo(""));
        Assert.That(lines[^1], Is.EqualTo("after"));
    }

    // ───────────────────────── one table per section ─────────────────────────

    private static readonly string[] Payors =
        { "Acme Health Plans", "Apex Health Inc", "Bluebird Health Shield", "Cedarvale", "Citrine Valley Healthcare", "Hollowbrook", "Kestrel", "Moonstone Health", "Tamarack Federal Plan", "Unity Crest Healthcare", "Willowbend Health Plans" };

    /// <summary>Module A with 13 planned steps and 13 Reference/Target steps (each a Name-order change).</summary>
    private void UseThirteenSteps()
    {
        var plan = ReadJson("state-map/plan.json");
        var ps = new JsonArray();
        for (var i = 1; i <= 13; i++)
            ps.Add(new JsonObject { ["id"] = $"Step{i}", ["action"] = $"Action {i}", ["changes"] = new JsonArray(), ["staysSame"] = new JsonArray(), ["checks"] = new JsonArray(), ["checksText"] = new JsonArray($"Check {i}") });
        plan["modules"]![0]!["steps"] = ps;
        plan["modules"]![0]!["plannedSteps"] = 13;
        WriteJson("state-map/plan.json", plan);
        foreach (var sys in new[] { "Reference", "Target" })
        {
            var r = ReadJson($"runs/ModuleA/{sys}/results.json");
            var steps = new JsonArray();
            for (var i = 1; i <= 13; i++)
                steps.Add(new JsonObject
                {
                    ["step"] = $"Step{i}", ["status"] = "PASS", ["description"] = $"Step {i}.", ["title"] = $"Sort {i}", ["actionMs"] = 100 + i, ["stepMs"] = 200 + i,
                    ["assertions"] = new JsonArray(new JsonObject
                    {
                        ["field"] = "grid.column[Name].sequence", ["type"] = "CHANGES", ["expected"] = string.Join(",", Payors.Reverse()),
                        ["actual"] = string.Join(",", Payors), ["expectedIsConstant"] = false, ["passed"] = true, ["message"] = ""
                    })
                });
            r["steps"] = steps;
            WriteJson($"runs/ModuleA/{sys}/results.json", r);
        }
    }

    private static List<int> TableRowCounts(string md)
    {
        var counts = new List<int>();
        var lines = Lines(md);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith('|') || (i > 0 && lines[i - 1].StartsWith('|'))) continue;
            var j = i;
            while (j < lines.Length && lines[j].StartsWith('|')) j++;
            counts.Add(j - i - 2);
        }
        return counts;
    }

    [Test]
    public void ModuleResult_ThirteenSteps_OneTablePerSection()
    {
        UseThirteenSteps();
        var (parts, _) = ChatRenderer.RenderParts(_pageDir, "module-result", "A", Progress, Now);
        Assert.That(parts.Select(p => p.Suffix), Is.EqualTo(new[] { "" }), "one message, never split");
        var md = parts[0].Markdown;
        var lines = Lines(md);
        Assert.That(lines[0], Is.EqualTo("### Module A: Load the page and sort the grid"));
        Assert.That(lines[2], Is.EqualTo("PayorList · Phase 5 of 5: Report"));
        Assert.That(lines[4], Is.EqualTo("Run id not recorded · elapsed 11:30 · waiting for you"));
        Assert.That(md, Does.Not.Contain("(steps "), "no part labels");
        Assert.That(md, Does.Not.Contain("(step "));

        // One heading per section, each followed by exactly one table holding every step.
        foreach (var section in new[] { "Reference Step Report", "Target Step Report", "Performance" })
        {
            Assert.That(lines.Count(l => l == "#### " + section), Is.EqualTo(1), section);
            var at = Array.IndexOf(lines, "#### " + section);
            var next = Array.FindIndex(lines, at + 1, l => l.StartsWith('#'));
            var body = string.Join("\n", lines[at..(next < 0 ? lines.Length : next)]);
            var counts = TableRowCounts(body);
            Assert.That(counts, Has.Count.EqualTo(1), section + ": one table");
            Assert.That(counts[0], Is.EqualTo(section == "Performance" ? 14 : 13), section + ": all steps (+ Total row)");
        }
        // Step description: the list is summarized in plain English.
        Assert.That(md, Does.Contain("| Step1 | PASS | Sort 1. Name column order changed: now A to Z (first: Acme Health Plans). |"));
        Assert.That(md, Does.Contain("| Step13 | PASS |"));
        Assert.That(md, Does.Not.Contain("Apex Health Inc,"));
        Assert.That(md, Does.Contain("Summary: "));
    }

    [Test]
    public void Plan_ModulePart_IsOneTable()
    {
        UseThirteenSteps();
        var (plan, _) = ChatRenderer.RenderParts(_pageDir, "plan", null, Progress, Now);
        Assert.That(plan.Select(p => p.Suffix), Is.EqualTo(new[] { "", "A", "B" }), "overview + one part per module");
        var a = plan[1].Markdown;
        Assert.That(Lines(a)[0], Is.EqualTo("### Test plan - Module A: Load the page and sort the grid"));
        Assert.That(TableRowCounts(a), Is.EqualTo(new[] { 13 }));
        Assert.That(a, Does.Contain("| Step13 | Action 13 | Check 13. |"));
    }

    [Test]
    public void EveryChatPart_EveryCellWithinDescriptionCap()
    {
        UseThirteenSteps();
        foreach (var view in ChatRenderer.Views)
        {
            var (parts, _) = ChatRenderer.RenderParts(_pageDir, view, view == "module-result" ? "A" : null, Progress, Now);
            foreach (var p in parts)
                foreach (var l in Lines(p.Markdown).Where(l => l.StartsWith('|')))
                    foreach (var cell in Md.SplitRow(l))
                        Assert.That(cell.Length, Is.LessThanOrEqualTo(Md.DescriptionCap), $"{view}-{p.Suffix}: {cell}");
        }
    }

    [Test]
    public void SequenceSummary_AscendingDescendingAndOther()
    {
        Assert.That(FieldLabels.SequenceText(string.Join(",", Payors)), Is.EqualTo("A to Z (first: Acme Health Plans)"));
        Assert.That(FieldLabels.SequenceText(string.Join(",", Payors.Reverse())), Is.EqualTo("Z to A (first: Willowbend Health Plans)"));
        Assert.That(FieldLabels.SequenceText("Citrine,Acme,Hollowbrook"), Is.EqualTo("in no sort order (first: Citrine, last: Hollowbrook)"));
        Assert.That(FieldLabels.SequenceText("1,2,10"), Is.EqualTo("lowest to highest (first: 1)"), "numbers compare as numbers");
        Assert.That(FieldLabels.SequenceText("Acme,Bluebird,U..."), Is.EqualTo("A to Z (first: Acme)"), "a truncated tail is ignored");

        var labels = new FieldLabels();
        RunStep Step(string type, string expected, string actual, bool passed = true) => new()
        {
            Step = "Step1", Title = "Sort by Name",
            Assertions = { new RunAssertion { Field = "grid.column[Name].sequence", Type = type, Expected = expected, Actual = actual, Passed = passed } }
        };
        var asc = string.Join(",", Payors);
        var desc = string.Join(",", Payors.Reverse());
        Assert.That(labels.Describe(Step("CHANGES", asc, desc), "PASS", false, summarizeLists: true),
            Is.EqualTo("Sort by Name. Name column order changed: now Z to A (first: Willowbend Health Plans)."));
        Assert.That(labels.Describe(Step("CHANGES", asc, "Citrine,Acme,Hollowbrook"), "PASS", false, summarizeLists: true),
            Is.EqualTo("Sort by Name. Name column order changed (first: Citrine, last: Hollowbrook)."));
        Assert.That(labels.Describe(Step("EQUALS", asc, desc, passed: false), "FAIL", true, summarizeLists: true),
            Does.StartWith("Name column order is Z to A (first: Willowbend Health Plans), expected A to Z (first: Acme Health Plans) (Reference)."));
        // Other long lists: item count with first and last; short lists stay verbatim.
        var map = new RunStep { Step = "Step2", Title = "Read the headers", Assertions =
        {
            new RunAssertion { Field = "grid.columnMap", Type = "EQUALS", Expected = "0:ID|1:Payor Name|2:Code|3:Category|4:Effective Date|5:Status", Actual = "0:ID|1:Payor Name|2:Code|3:Category|4:Effective Date|5:Status", Passed = true },
            new RunAssertion { Field = "grid.rowIds", Type = "EQUALS", Expected = "1,2,3", Actual = "1,2,3", Passed = true }
        } };
        Assert.That(labels.Describe(map, "PASS", false, summarizeLists: true),
            Is.EqualTo("Read the headers. Grid column map shows 6 items (first: 0:ID, last: 5:Status). Grid row ids is '1,2,3'."));
        // The audit text (no summarizeLists) keeps the values.
        Assert.That(labels.Describe(Step("CHANGES", asc, desc), "PASS", false), Does.Contain("'Willowbend Health Plans,"));
    }

    // ─────────────────────────── module-result ───────────────────────────

    [Test]
    public void ModuleResult_ReadOnly_StepReportsPerformanceGapsCssAndSummary()
    {
        var (md, summary) = Render("module-result", "A");
        Assert.That(md, Does.Contain("Data effect: read-only (ran in parallel)"));
        Assert.That(md, Does.Contain("### Reference Step Report"));
        Assert.That(md, Does.Contain("### Target Step Report"));
        Assert.That(md, Does.Contain("| Step | Status | Description |"));
        // [review F3] + P7: FAIL rows lead with the mismatch in plain English.
        Assert.That(md, Does.Contain("| Step1 | FAIL | Pager text is '1-10 of 15', expected 'View 1 - 10 of 15' (Reference). Open /Pages/PayorList and read the first page. Rows shown is 10. |"));
        Assert.That(md, Does.Contain("| Step2 | PASS | Click the Name header. Payor Name in grid row 1 changed from 'Acme' to 'Bluebird'. Pager text stayed '1-10 of 15'. |"));
        Assert.That(md, Does.Contain("| Step | Old page (Reference) ms | New page (Target) ms |"));
        Assert.That(md, Does.Contain(ChatRenderer.PerformancePurpose));
        Assert.That(md, Does.Contain("| Step2 | 612 | 540 |"));
        Assert.That(md, Does.Contain("| Step2 | 612 | 540 |" + Environment.NewLine + "| Total | 5055 | 5429 |"), "total row stays inside the table");
        Assert.That(md, Does.Contain("| ID | Severity | What | Old page (Reference) | New page (Target) |"));
        Assert.That(md, Does.Contain("| A-Step1-pager.range | MEDIUM | Pager text (Step1) | View 1 - 10 of 15 | 1-10 of 15 |"));
        Assert.That(md, Does.Contain("| Issue | Group | Elements | Property | Reference | Target | Severity | Seen in |"));
        Assert.That(md, Does.Contain("| The clear button looks different when the page first loads: text is black on the old page (Reference) and white on the new page (Target). | G1 | 1: ClearButton | color | rgb(0, 0, 0) | rgb(255, 255, 255) | MEDIUM | A [page_load] |"));
        Assert.That(md, Does.Contain("What this means: the new page (Target) is not at parity yet for this module: 1 step failed on the new page, 1 functional gap is open and 1 styling/layout issue is open."));
        Assert.That(md, Does.Not.Contain("| 1: Grid column header | font-weight"), "accepted diffs are not listed as actionable");
        Assert.That(md, Does.Contain("1 accepted differences in `reports/ModuleA_CSS_Diff.md`"));
        const string text = "Reference 3/3 steps passed (4/4 assertions), baseline PASS; Target 2/3 steps passed (3/4 assertions), module FAIL (1 step failed, functional gaps open, styling/layout open); 1 gaps (H 0 / M 1 / L 0); 1 actionable CSS groups (1 raw rows actionable, 2 total, 1 accepted); Reference 5.1s vs Target 5.4s.";
        Assert.That(md, Does.Contain("Summary: " + text));
        Assert.That(summary, Is.EqualTo("Module A: " + text));
    }

    [Test]
    public void ModuleMeaning_PassAndSharedCssRows()
    {
        Assert.That(ChatRenderer.ModuleMeaning(new ModuleSummary { TargetStatus = "PASS" }, new RunResultsFile()),
            Is.EqualTo("What this means: the new page (Target) matches the old page (Reference) for this module. What to do: nothing."));
        var shared = new ModuleSummary { TargetStatus = "FAIL", CssDiffs = { new CssDiffEntry { Actionable = true }, new CssDiffEntry { Actionable = true } } };
        Assert.That(ChatRenderer.ModuleMeaning(shared, new RunResultsFile()),
            Does.StartWith("What this means: the new page (Target) is not at parity yet for this module: 2 styling rows of this module belong to styling issues counted in another module."));
    }

    [Test]
    public void ModuleMeaning_NamesCountedOldPageDefects()
    {
        var s = new ModuleSummary
        {
            TargetStatus = "FAIL", ActionableCssDiffs = 3, CssGroupsOwned = { "G1", "G2", "G3" },
            CssDiffs =
            {
                new CssDiffEntry { Actionable = true, GroupId = "G1", Action = "Reference larger: layout.gapToPager Reference 508.5px vs Target 13.5px" },
                new CssDiffEntry { Actionable = true, GroupId = "G2", Action = "Align to reference: set color: rgb(0, 0, 0)" },
                new CssDiffEntry { Actionable = true, GroupId = "G9", Action = "Reference larger: not owned here" }
            }
        };
        var text = ChatRenderer.ModuleMeaning(s, new RunResultsFile());
        Assert.That(text, Does.Contain("3 styling/layout issues are open (1 of them is an old-page defect to accept or confirm, not to copy)."));
        Assert.That(text, Does.Contain("What to do: fix the other issues in the new page"));
        Assert.That(text, Does.EndWith("confirm the old-page defects on the old page and accept them in state-map/css-map.json; never copy them into the new page."));
        Assert.That(FinalReportBuilder.Meaning("FAIL", 2, 1, 0, 3, 0, 1), Does.Contain("3 styling/layout issues (1 of them is an old-page defect to accept or confirm, not to copy) still open"));
    }

    [Test]
    public void SystemText_AllStepsPassed_ModuleFail_SaysWhatIsOpen()
    {
        var r = new RunResultsFile { Totals = { StepsPass = 13, AssertionsPassed = 55, AssertionsTotal = 55 } };
        for (var i = 0; i < 13; i++) r.Steps.Add(new RunStep { Step = $"Step{i}" });
        var s = new ModuleSummary { TargetStatus = "FAIL", ActionableCssDiffs = 2 };
        Assert.That(ChatRenderer.SystemText("Target", r, "FAIL", s),
            Is.EqualTo("Target 13/13 steps passed (55/55 assertions), module FAIL (styling/layout open)"));
        Assert.That(ChatRenderer.SystemText("Reference", r, "PASS"), Is.EqualTo("Reference 13/13 steps passed (55/55 assertions), baseline PASS"));
    }

    [Test]
    public void ModuleResult_MissingDataEffect_DefaultsToMutating_AndEmptySourcesShowNone()
    {
        var md = Render("module-result", "B").Markdown;
        Assert.That(md, Does.Contain("Data effect: mutating (default: not set in plan) (serialized; service restarted after Reference and Target)"));
        var gaps = md[md.IndexOf("### Functional Gaps", StringComparison.Ordinal)..md.IndexOf("### CSS Diffs", StringComparison.Ordinal)];
        Assert.That(gaps, Does.Contain("| None found. |  |  |  |  |"));
        Assert.That(md, Does.Contain("| Issue | Group | Elements | Property | Reference | Target | Severity | Seen in |" + Environment.NewLine + "|---|---|---|---|---|---|---|---|" + Environment.NewLine + "| None found. |"));
    }

    [Test]
    public void ModuleResult_MissingTargetResults_ShowsNotRun()
    {
        File.Delete(Path.Combine(_pageDir, "runs", "ModuleB", "Target", "results.json"));
        var md = Render("module-result", "B").Markdown;
        var target = md[md.IndexOf("### Target Step Report", StringComparison.Ordinal)..];
        Assert.That(Lines(target)[1], Is.EqualTo(""), "blank line after every heading");
        Assert.That(Lines(target)[2], Does.StartWith("The same steps on the new page (Target)"), "purpose line");
        Assert.That(Lines(target)[3], Is.EqualTo(""), "purpose line is its own paragraph");
        Assert.That(Lines(target)[4], Is.EqualTo("(not run)"));
    }

    [Test]
    public void ModuleResult_MoreThan25Steps_TruncatedWithReportPointer()
    {
        var r = ReadJson("runs/ModuleB/Reference/results.json");
        var steps = new JsonArray();
        for (var i = 1; i <= 30; i++)
            steps.Add(new JsonObject
            {
                ["step"] = $"Step{i}", ["status"] = "PASS", ["description"] = $"Click Next page ({i} -> {i + 1}). [3{i:00}ms]",
                ["title"] = $"Next {i}", ["actionMs"] = 3000 + i, ["stepMs"] = 3100 + i, ["assertions"] = new JsonArray()
            });
        r["steps"] = steps;
        WriteJson("runs/ModuleB/Reference/results.json", r);
        var md = Render("module-result", "B").Markdown;
        var refReport = md[md.IndexOf("### Reference Step Report", StringComparison.Ordinal)..md.IndexOf("Target Step Report", StringComparison.Ordinal)];
        Assert.That(refReport, Does.Contain("| Step25 | PASS |"));
        Assert.That(refReport, Does.Not.Contain("| Step26 | PASS |"));
        Assert.That(refReport, Does.Contain("| ... | 5 more in `reports/ModuleB_Parity.md` |"));
    }

    [Test]
    public void ModuleResult_LongFailRow_FailedCheckIsNotCutOff()
    {
        // [review F3] a long FAIL description must still lead with the mismatch; the cut happens at a sentence end.
        var r = ReadJson("runs/ModuleA/Target/results.json");
        var assertions = r["steps"]![1]!["assertions"]!.AsArray();
        for (var i = 1; i <= 12; i++)
            assertions.Add(new JsonObject
            {
                ["field"] = $"row[{i}].name.text", ["type"] = "CHANGES", ["expected"] = $"Payor {i}", ["actual"] = $"Payor {i} EDITED",
                ["expectedIsConstant"] = false, ["passed"] = true, ["message"] = ""
            });
        WriteJson("runs/ModuleA/Target/results.json", r);
        var md = Render("module-result", "A").Markdown;
        var row = Lines(md).First(l => l.StartsWith("| Step1 | FAIL |", StringComparison.Ordinal) && l.Contains("EDITED"));
        Assert.That(row, Does.StartWith("| Step1 | FAIL | Pager text is '1-10 of 15', expected 'View 1 - 10 of 15' (Reference). Open /Pages/PayorList"));
        Assert.That(row, Does.EndWith(". |"), "cut at a sentence end");
        Assert.That(row, Does.Not.Contain("..."));
    }

    [Test]
    public void ModuleResult_And_Final_CssStatesWithoutCssMapElements_SayNotCompared()
    {
        // [review F5] module A plans page_load + sorted; css-map.json maps only page_load.
        File.WriteAllText(Path.Combine(_pageDir, "state-map", "css-map.json"),
            """{ "elements": [ { "name": "ClearButton", "state": "page_load", "reference": "#btnClear", "target": ".clear-btn", "properties": { "color": "MEDIUM" } } ] }""");
        var (md, summary) = Render("module-result", "A");
        Assert.That(md, Does.Contain("Not compared: no `state-map/css-map.json` elements for state(s) sorted, so these states are not a clean result."));
        Assert.That(summary, Does.Contain("accepted, CSS not compared for sorted)"));
        var final = Render("final").Markdown;
        Assert.That(final, Does.Contain("Not compared (no state-map/css-map.json elements for these states): Module A: sorted"));

        // Without css-map.json the generator reports '(unmapped)' diffs, so no extra note.
        File.Delete(Path.Combine(_pageDir, "state-map", "css-map.json"));
        Assert.That(Render("module-result", "A").Markdown, Does.Not.Contain("Not compared:"));
    }

    [Test]
    public void ModuleResult_AbortedRun_AddsAbortRow()
    {
        var r = ReadJson("runs/ModuleA/Target/results.json");
        r["aborted"] = true;
        r["abortReason"] = "Timeout 30000ms exceeded waiting for '.mud-table-row'";
        WriteJson("runs/ModuleA/Target/results.json", r);
        var md = Render("module-result", "A").Markdown;
        Assert.That(md, Does.Contain("| - | FAIL | ABORTED: Timeout 30000ms exceeded waiting for '.mud-table-row' |"));
    }

    // ──────────────────────────── run-summary ────────────────────────────

    [Test]
    public void RunSummary_SelectsRunByCurrentRunId_ScheduleWallAndHarnessErrors()
    {
        var (md, summary) = Render("run-summary");
        Assert.That(md, Does.Contain("| Module | Old page (Reference) steps passed | New page (Target) steps passed | Functional gaps (H/M/L) | CSS issues | Old page ms | New page ms |"));
        Assert.That(md, Does.Contain("| A | 3/3 | 2/3 | 1 (0/1/0) | 1 | 5055 | 5429 |"));
        Assert.That(md, Does.Contain("| B | 2/2 | 2/2 | 0 (0/0/0) | 0 | 2091 | 1932 |"));
        // [review M1] runs[0] matches status.currentRun.runId although runs[1] is the last entry.
        Assert.That(md, Does.Contain("Schedule: parallel read-only A (max 4); serialized mutating B with 2 service restarts (14.2s, 13.8s)."));
        Assert.That(md, Does.Contain("Phase 3 (Run Reference + Target) wall time: 03:21 (build 41s, preflight 9s). Harness errors: render module-result B failed."));
        Assert.That(md, Does.Not.Contain("render run-summary failed"), "render-failed events of other runs are excluded");
        Assert.That(summary, Is.EqualTo("Run: 2 modules in 03:21; Ref PASS 2/2; Target PASS 1/2; 1 findings; 1 actionable CSS groups."));
    }

    [Test]
    public void RunSummary_UnknownCurrentRunId_FallsBackToLastRunsEntry()
    {
        var st = ReadJson("status.json");
        st["currentRun"]!["runId"] = "20990101-000000";
        WriteJson("status.json", st);
        var tm = ReadJson("timings.json");
        tm["runs"]![1]!["action"] = "run-page"; // the last entry is a run-page run here
        WriteJson("timings.json", tm);
        var (md, summary) = Render("run-summary");
        Assert.That(md, Does.Contain("Schedule: parallel read-only A (max 2); serialized mutating none with no service restarts."));
        Assert.That(md, Does.Contain("Phase 3 (Run Reference + Target) wall time: 00:35 (build 5s, preflight 3s)."));
        Assert.That(summary, Does.StartWith("Run: 1 modules in 00:35;"));
    }

    [Test]
    public void RunSummary_CurrentRunIsReportRun_SelectsLastRunPageEntry()
    {
        // [review F7] after -Action report, status.currentRun is the report run; run-summary must still show the run-page run.
        var tm = ReadJson("timings.json");
        tm["runs"]!.AsArray().Add(new JsonObject
        {
            ["runId"] = "20261027-102000", ["action"] = "report", ["startedAt"] = "2026-10-27T10:20:00.0000000+05:30",
            ["finishedAt"] = "2026-10-27T10:20:01.0000000+05:30", ["modules"] = new JsonArray()
        });
        WriteJson("timings.json", tm);
        var st = ReadJson("status.json");
        st["currentRun"]!["runId"] = "20261027-102000";
        WriteJson("status.json", st);
        var (md, summary) = Render("run-summary");
        Assert.That(md, Does.Contain("Phase 3 (Run Reference + Target) wall time: 03:21 (build 41s, preflight 9s)."));
        Assert.That(summary, Does.StartWith("Run: 2 modules in 03:21;"));

        // Unknown runId: the last run-page entry (runs[0]), not the trailing report / rerun-target entries.
        st["currentRun"]!["runId"] = "20990101-000000";
        WriteJson("status.json", st);
        Assert.That(Render("run-summary").Summary, Does.StartWith("Run: 2 modules in 03:21;"));
    }

    [Test]
    public void RunSummary_RunInProgress_UsesLifecycleRunWallMsEnv()
    {
        var tm = ReadJson("timings.json");
        tm["runs"]![0]!.AsObject().Remove("finishedAt");
        WriteJson("timings.json", tm);
        Environment.SetEnvironmentVariable("LIFECYCLE_RUN_WALL_MS", "187000");
        var (md, summary) = Render("run-summary");
        Assert.That(md, Does.Contain("Phase 3 (Run Reference + Target) wall time: 03:07 (build 41s, preflight 9s)."));
        Assert.That(summary, Does.StartWith("Run: 2 modules in 03:07;"));

        Environment.SetEnvironmentVariable("LIFECYCLE_RUN_WALL_MS", null);
        Assert.That(Render("run-summary").Markdown, Does.Contain("Phase 3 (Run Reference + Target) wall time: not measured"));
    }

    [Test]
    public void RunSummary_HarnessErrorAndNotRunModule()
    {
        var st = ReadJson("status.json");
        st["modules"]!["B"]!["harnessError"] = "build failed: PayorListModuleB_v2.cs(10,5): error CS0103";
        st["modules"]!["B"]!["target"]!["status"] = "NOT_RUN";
        st["modules"]!["B"]!["target"]!["stepsPass"] = 0;
        st["modules"]!["B"]!["target"]!["stepsFail"] = 0;
        WriteJson("status.json", st);
        var md = Render("run-summary").Markdown;
        Assert.That(md, Does.Contain("| B | 2/2 | NOT_RUN |"));
        Assert.That(md, Does.Contain("Module B: build failed: PayorListModuleB_v2.cs(10,5): error CS0103"));
    }

    // ───────────────────────────── fix-round ─────────────────────────────

    [Test]
    public void FixRound_RowsRerunResultModuleTotalsAndSummary()
    {
        var (md, summary) = Render("fix-round");
        Assert.That(md, Does.Contain("| Finding | Module | What | Kind | File | Change | Before -> After | Rerun result |"));
        Assert.That(md, Does.Contain("| A-Step1-grid.rowCount | A | Rows shown (Step1) | app | sample/PayorContracts.Web/Components/Pages/PayorList.razor | Default page size matches Reference (10 rows) | `RowsPerPage=\"5\"` -> `RowsPerPage=\"10\"` | RESOLVED |"));
        Assert.That(md, Does.Contain("| A-Step1-pager.range | A | Pager text (Step1) |"));
        Assert.That(md, Does.Contain("`InfoFormat=\"{first_item}-{last_item} of {all_items}\"` -> `InfoFormat=\"View {first_item} - {last_item} of {all_items}\"` | STILL OPEN |"));
        Assert.That(md, Does.Contain("| css:ClearButton.color | A | CSS ClearButton.color | css |"));
        Assert.That(md, Does.Contain("Module totals: A findings 2 -> 1, CSS 1 -> 1, Target 2/3 steps passed, module FAIL (1 step failed, functional gaps open, styling/layout open)."));
        Assert.That(summary, Is.EqualTo("Round 1: 1/3 edits resolved their findings; open findings 2 -> 1."));
    }

    [Test]
    public void FixRound_StepsPassButCssOpen_ModuleTotalsNeverSayModulePass()
    {
        var st = ReadJson("status.json");
        st["modules"]!["A"]!["target"]!["status"] = "PASS";
        st["modules"]!["A"]!["target"]!["stepsPass"] = 3;
        st["modules"]!["A"]!["target"]!["stepsFail"] = 0;
        WriteJson("status.json", st);
        var md = Render("fix-round").Markdown;
        Assert.That(md, Does.Contain("Target 3/3 steps passed, module FAIL (functional gaps open, styling/layout open)."));
        Assert.That(md, Does.Not.Contain("module PASS"));
    }

    [Test]
    public void FixRound_NoFixes_ShowsNone()
    {
        File.Delete(Path.Combine(_pageDir, "fixes.json"));
        var (md, summary) = Render("fix-round");
        Assert.That(md, Does.Contain("| None found. |  |  |  |  |  |  |  |"));
        Assert.That(summary, Is.EqualTo("Round 1: 0/0 edits resolved their findings; open findings 2 -> 1."));
    }

    // ─────────────────────────────── final ───────────────────────────────

    [Test]
    public void Final_SectionsSummaryFooterAndRewritesFinalReport()
    {
        var (md, summary) = Render("final");
        foreach (var h in new[] { "Executive Summary", "Module Results", "Functional Gaps", "CSS Comparison", "Bug Fixes", "Manual Tests", "Limitations" })
            Assert.That(Lines(md), Does.Contain("#### " + h), h);
        Assert.That(md, Does.Contain("Overall: FAIL"));
        Assert.That(md, Does.Contain("Total lifecycle time: 11:30 of 15:00 target (gate wait excluded: 02:30) - WITHIN TARGET."));
        Assert.That(md, Does.Contain("| Phase | Target | Measured | Status |"));
        Assert.That(md, Does.Contain("| Phase 1 (Plan) | 04:00 | 03:00 | done |"));
        Assert.That(md, Does.Contain("| Orchestration (between phases) | - | 01:00 | - |"));
        Assert.That(md, Does.Contain("| Total | 15:00 | 11:30 | WITHIN TARGET |"));
        var path = Path.Combine(_pageDir, "reports", "FinalReport.md");
        Assert.That(md, Does.Contain("| Document | Path |"));
        Assert.That(md, Does.Contain("| Full report (markdown) | `" + path + "` |"));
        Assert.That(File.Exists(path), Is.True, "render final rewrites FinalReport.md");
        Assert.That(File.ReadAllText(path), Does.Contain("Total lifecycle time: 11:30 of 15:00 target"), "same timings as the chat view");
        Assert.That(summary, Is.EqualTo("Final: FAIL; 2/2 Ref PASS, 1/2 Target PASS, 1 open findings; total 11:30 of 15:00."));
    }

    [Test]
    public void Final_DocumentPathsAreInlineCodeInATable()
    {
        var reports = Path.Combine(_pageDir, "reports");
        Directory.CreateDirectory(reports);
        var pdf = Path.Combine(reports, "FinalReport.pdf");
        File.WriteAllText(pdf, "x");
        var (md, _) = Render("final");
        Assert.That(md, Does.Contain("| FinalReport.pdf | `" + pdf + "` |"));
        // No bare absolute path anywhere: markdown would drop the '\' before '.kiro'.
        foreach (var line in md.Split('\n').Where(l => l.Contains(_pageDir)))
            Assert.That(line, Does.Contain("`" + _pageDir), "path not in backticks: " + line);
    }

    // ────────────────────────── LifecycleTimings ─────────────────────────

    [Test]
    public void Timings_FinalGatePendingTenMinutesAfterFinishedAt_LeavesElapsedUnchanged()
    {
        var node = ReadJson("timings.json");
        var finished = DateTimeOffset.Parse("2026-10-27T10:14:00.0000000+05:30");
        var atFinish = LifecycleTimings.FromNode(node, finished);
        var later = LifecycleTimings.FromNode(node, finished.AddMinutes(10));
        Assert.That(atFinish.ElapsedMs, Is.EqualTo(690000));
        Assert.That(later.ElapsedMs, Is.EqualTo(atFinish.ElapsedMs));
        Assert.That(later.GateWaitMs, Is.EqualTo(atFinish.GateWaitMs));
        Assert.That(later.GatePending, Is.True);
        Assert.That(later.PhaseLine(), Is.EqualTo("Phase 5 of 5 (Report) - elapsed 11:30 - estimated remaining 00:00 - waiting for you"));
    }

    [Test]
    public void Timings_PlanGatePendedTwice_SumsBothClippedEntries_AndCountsRevisionSegment()
    {
        var t = LifecycleTimings.FromNode(ReadJson("timings.json"), Now);
        // plan_signoff 60 s + plan_signoff 60 s + final_signoff clipped to finishedAt (10:13:30 -> 10:14:00) 30 s.
        Assert.That(t.GateWaitMs, Is.EqualTo(60000 + 60000 + 30000));
        // Phase 0: first segment 2 min + revision segment 1 min.
        Assert.That(t.Phases[0].ActiveMs, Is.EqualTo(180000));
        Assert.That(t.OrchestrationMs, Is.EqualTo(60000));
    }

    [Test]
    public void Timings_GateBeforeStart_IsClippedToLifecycleWindow()
    {
        var start = DateTimeOffset.Parse("2026-10-27T10:00:00+05:30");
        var ms = LifecycleTimings.GateMs(start.AddMinutes(-5), start.AddMinutes(1), start, start.AddMinutes(20), start.AddMinutes(30));
        Assert.That(ms, Is.EqualTo(60000));
        Assert.That(LifecycleTimings.GateMs(start.AddMinutes(25), null, start, start.AddMinutes(20), start.AddMinutes(30)), Is.EqualTo(0));
    }

    [Test]
    public void Timings_RunningPhase_RemainingUsesPreviousLifecycleEstimate()
    {
        var json = """
        { "lifecycles": [
          { "id": "old", "startedAt": "2026-10-26T10:00:00+05:30", "finishedAt": "2026-10-26T10:12:00+05:30",
            "phases": [ { "id": 0, "status": "done", "segments": [ { "start": "2026-10-26T10:00:00+05:30", "end": "2026-10-26T10:03:00+05:30" } ] },
                        { "id": 1, "status": "done", "segments": [ { "start": "2026-10-26T10:03:00+05:30", "end": "2026-10-26T10:05:00+05:30" } ] },
                        { "id": 2, "status": "done", "segments": [ { "start": "2026-10-26T10:05:00+05:30", "end": "2026-10-26T10:08:00+05:30" } ] },
                        { "id": 3, "status": "skipped", "segments": [] },
                        { "id": 4, "status": "done", "segments": [ { "start": "2026-10-26T10:08:00+05:30", "end": "2026-10-26T10:09:00+05:30" } ] } ],
            "gates": [] },
          { "id": "new", "startedAt": "2026-10-27T10:00:00+05:30", "finishedAt": null,
            "phases": [ { "id": 0, "status": "done", "segments": [ { "start": "2026-10-27T10:00:00+05:30", "end": "2026-10-27T10:02:00+05:30" } ] },
                        { "id": 1, "status": "running", "segments": [ { "start": "2026-10-27T10:02:00+05:30", "end": null } ] },
                        { "id": 2, "status": "pending", "segments": [] }, { "id": 3, "status": "pending", "segments": [] }, { "id": 4, "status": "pending", "segments": [] } ],
            "gates": [] } ] }
        """;
        var t = LifecycleTimings.FromNode(JsonNode.Parse(json), DateTimeOffset.Parse("2026-10-27T10:03:00+05:30"));
        // Phase 1 estimate 2:00 (previous) - 1:00 active = 1:00, + phase 2 3:00 + phase 3 0:00 (skipped) + phase 4 1:00 = 5:00.
        Assert.That(t.RemainingMs, Is.EqualTo(300000));
        Assert.That(t.PhaseLine(), Is.EqualTo("Phase 2 of 5 (Author tests) - elapsed 03:00 - estimated remaining 05:00"));
    }

    [Test]
    public void Timings_NoLifecycle_FallbackStrings()
    {
        File.Delete(Path.Combine(_pageDir, "timings.json"));
        var t = LifecycleTimings.Load(_pageDir, Now);
        Assert.That(t.HasLifecycle, Is.False);
        Assert.That(t.PhaseLine(), Is.EqualTo("Phase timing not measured (no lifecycle recorded)"));
        Assert.That(LifecycleTimings.Mmss(965000), Is.EqualTo("16:05"));
    }
}

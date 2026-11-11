using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>Browser-free tests for FinalReportBuilder (design §5.6, §5.7, §9) against Fixtures/PayorListMini.</summary>
[TestFixture]
public class FinalReportBuilderTests
{
    private string _pageDir = "";

    [SetUp]
    public void SetUp() => _pageDir = ChatRendererTests.CopyFixture();

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_pageDir)) Directory.Delete(Path.GetDirectoryName(_pageDir)!, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    private string Build() => File.ReadAllText(FinalReportBuilder.WriteFromDisk(_pageDir, ChatRendererTests.Now));

    private JsonNode ReadJson(string rel) => JsonNode.Parse(File.ReadAllText(Path.Combine(_pageDir, rel)))!;
    private void WriteJson(string rel, JsonNode node) => File.WriteAllText(Path.Combine(_pageDir, rel), node.ToJsonString());

    private void MakeModuleAClean()
    {
        var s = ReadJson("runs/ModuleA/summary.json");
        s["targetStatus"] = "PASS";
        s["openFindings"] = 0;
        s["actionableCssDiffs"] = 0;
        s["findings"] = new JsonArray();
        var css = s["cssDiffs"]!.AsArray();
        css.RemoveAt(0); // drop the actionable ClearButton diff, keep the accepted one
        s["cssDiffsTotal"] = 1;
        WriteJson("runs/ModuleA/summary.json", s);
    }

    [Test]
    public void CssComparison_AutoRows_AuditPropertyText_ProfileAcceptance()
    {
        ChatRendererTests.AddAutoRows(_pageDir);
        var md = Build();
        Assert.That(md, Does.Contain("| G1 | 1: button 'Search' | reading order (layout.order) | position 1 of 2 | position 2 of 2 | HIGH | A [page_load] | UNRESOLVED (Module A) |"));
        Assert.That(md, Does.Contain(" | 1: button 'Save' | cursor when disabled (disabled.cursor) | default | not-allowed | MEDIUM | A [page_load] | accepted by profile webforms-jqgrid__blazor-mudblazor: MudBlazor default |"));
        Assert.That(md, Does.Contain("Stack profile: webforms-jqgrid__blazor-mudblazor, 1 rows accepted by profile"));
    }

    [Test]
    public void AllSevenFinalSections_AndPerModuleFiveTargetSections_ArePresent()
    {
        var md = Build();
        foreach (var h in new[] { "### Executive Summary", "### Module Results", "### Functional Gaps", "### CSS Comparison", "### Bug Fixes", "### Manual Tests", "### Limitations" })
            Assert.That(md, Does.Contain(h), h);
        Assert.That(md, Does.Contain("## Module A - Load the page and sort the grid"));
        Assert.That(md, Does.Contain("## Module B - Edit a cell and Save All"));
        // Reference baseline 3 sections + Target 5 sections (BuildParity, headings demoted one level).
        foreach (var h in new[] { "### Reference baseline", "#### 1. Step Report", "#### 2. HTML Diff Report", "#### 3. Summary", "### Target",
                     "#### 2. Performance Comparison", "#### 3. Functional Gaps", "#### 4. CSS Diff Report", "#### 5. Summary", "### Baseline vs Target values" })
            Assert.That(md, Does.Contain(h), h);
        Assert.That(md, Does.Not.Contain("# Module A Parity Report"));
        Assert.That(md, Does.Contain("## UNRESOLVED"));
        Assert.That(md, Does.Contain("## NOT_RUN"));
        Assert.That(md, Does.Contain("View 1 - 10 of 15"), "real values from results/summary");
    }

    [Test]
    public void OpenFinding_OverallFail_WithRealValues()
    {
        var md = Build();
        Assert.That(md, Does.Contain("Overall: FAIL"));
        Assert.That(md, Does.Contain("Modules 2; Reference PASS 2; Target PASS 1; open findings 1; actionable CSS groups 1 (raw rows 1); fixes applied 3; unresolved 2."));
        Assert.That(md, Does.Contain("What this means: the new page (Target) is not at parity yet: 1 functional gap and 1 styling/layout issue still open. What to do:"));
        Assert.That(md, Does.Contain("| Issue | Group | Elements | Property | Reference | Target | Severity | Seen in | Action |"));
        Assert.That(md, Does.Contain("| Module | Workflow | Data effect | Reference | Target | Open findings | CSS actionable | Fixes | Result |"));
        Assert.That(md, Does.Contain("| A | Load the page and sort the grid | read-only | PASS (3/3) | FAIL (2/3) | 1 | 1 | 3 | FAIL |"));
        Assert.That(md, Does.Contain("| B | Edit a cell and Save All | mutating (default: not set in plan) | PASS (2/2) | PASS (2/2) | 0 | 0 | 0 | PASS |"));
        Assert.That(md, Does.Contain("| Module | ID | Severity | Step | What | Reference | Target | Status |"));
        Assert.That(md, Does.Contain("| A | A-Step1-pager.range | MEDIUM | Step1 | Pager text (field: pager.range) | View 1 - 10 of 15 | 1-10 of 15 | UNRESOLVED |"));
        Assert.That(md, Does.Contain("| A | A-Step1-grid.rowCount | - | Step1 | Rows shown (field: grid.rowCount) | - | - | RESOLVED (round 1) |"));
        Assert.That(md, Does.Contain("| Group | Elements | Property | Reference | Target | Severity | Seen in | Action |"));
        Assert.That(md, Does.Contain("| G1 | 1: ClearButton | color | rgb(0, 0, 0) | rgb(255, 255, 255) | MEDIUM | A [page_load] | UNRESOLVED (Module A) |"));
        Assert.That(md, Does.Contain(" | 1: Grid column header | font-weight | 600 | 500 | LOW | A [page_load] | accepted: Material header weight 500 vs legacy 600, agreed acceptable |"));
        Assert.That(md, Does.Contain("Resolved in fix rounds: 0"));
        Assert.That(md, Does.Contain("| Round | Module | Kind | File | Before -> After |"));
        Assert.That(md, Does.Contain("| 1 | A | app | sample/PayorContracts.Web/Components/Pages/PayorList.razor | `RowsPerPage=\"5\"` -> `RowsPerPage=\"10\"` |"));
        Assert.That(md, Does.Contain("| Module | Item | Reason |"));
        Assert.That(md, Does.Contain("| A | Sort-indicator glyph visibility |"));
        var unresolved = md[md.IndexOf("## UNRESOLVED", StringComparison.Ordinal)..md.IndexOf("## NOT_RUN", StringComparison.Ordinal)];
        Assert.That(unresolved, Does.Contain("| A | A-Step1-pager.range | Step1 | pager.range | View 1 - 10 of 15 | 1-10 of 15 | app |"));
        var notRun = md[md.IndexOf("## NOT_RUN", StringComparison.Ordinal)..];
        Assert.That(notRun, Does.Contain("None."));
    }

    [Test]
    public void EverythingClean_OverallPass()
    {
        MakeModuleAClean();
        var md = Build();
        Assert.That(md, Does.Contain("Overall: PASS"));
        Assert.That(md, Does.Contain("Modules 2; Reference PASS 2; Target PASS 2; open findings 0; actionable CSS groups 0 (raw rows 0); fixes applied 3; unresolved 0."));
        Assert.That(md, Does.Contain("What this means: the new page (Target) matches the old page (Reference) in every module."));
        Assert.That(md, Does.Contain("| A | A-Step1-pager.range | - | Step1 | Pager text (field: pager.range) | - | - | RESOLVED (round 1) |"));
        Assert.That(md, Does.Contain("Resolved in fix rounds: 1"));
        var unresolved = md[md.IndexOf("## UNRESOLVED", StringComparison.Ordinal)..md.IndexOf("## NOT_RUN", StringComparison.Ordinal)];
        Assert.That(unresolved, Does.Contain("None."));
    }

    [Test]
    public void ResolvedFinding_CarriesSeverityAndValuesFromPreRoundSnapshot()
    {
        // [review F4] rerun-target writes runs/ModuleX/summary.before-round{N}.json before the round's rerun.
        File.Copy(Path.Combine(_pageDir, "runs", "ModuleA", "summary.json"), Path.Combine(_pageDir, "runs", "ModuleA", "summary.before-round1.json"));
        MakeModuleAClean();
        var md = Build();
        Assert.That(md, Does.Contain("| A | A-Step1-pager.range | MEDIUM | Step1 | Pager text (field: pager.range) | View 1 - 10 of 15 | 1-10 of 15 | RESOLVED (round 1) |"));
    }

    [Test]
    public void ResolvedFinding_FromAnotherModule_IsListedUnderItsOwnModule()
    {
        // One Module A fix can resolve Module B findings too: the Module column follows the finding ID prefix.
        var fixes = ReadJson("fixes.json").AsArray();
        fixes[0]!["findingIds"]!.AsArray().Add("B-Step2-grid.rowCount");
        WriteJson("fixes.json", fixes);
        var md = Build();
        Assert.That(md, Does.Contain("| B | B-Step2-grid.rowCount | - | Step2 | Rows shown (field: grid.rowCount) | - | - | RESOLVED (round 1) |"));
        Assert.That(md, Does.Not.Contain("| A | B-Step2-grid.rowCount |"));
    }

    [Test]
    public void HarnessError_ModuleIsNotRun_AndOverallFail()
    {
        MakeModuleAClean();
        var st = ReadJson("status.json");
        st["modules"]!["B"]!["harnessError"] = "build failed: PayorListModuleB_v2.cs(10,5): error CS0103";
        WriteJson("status.json", st);
        var md = Build();
        Assert.That(md, Does.Contain("Overall: FAIL"));
        Assert.That(md, Does.Contain("| B | Edit a cell and Save All | mutating (default: not set in plan) | PASS (2/2) | PASS (2/2) | 0 | 0 | 0 | NOT_RUN |"));
        Assert.That(md, Does.Contain("- Module B NOT_RUN: build failed: PayorListModuleB_v2.cs(10,5): error CS0103; logs: logs/build-B.log, logs/test-B-*.log"));
        var notRun = md[md.IndexOf("## NOT_RUN", StringComparison.Ordinal)..];
        Assert.That(notRun, Does.Contain("- Module B: build failed: PayorListModuleB_v2.cs(10,5): error CS0103; logs: logs/build-B.log"));
    }

    [Test]
    public void PlannedModuleWithoutSummary_IsNotRun()
    {
        MakeModuleAClean();
        File.Delete(Path.Combine(_pageDir, "runs", "ModuleB", "summary.json"));
        var md = Build();
        Assert.That(md, Does.Contain("Overall: FAIL"));
        Assert.That(md, Does.Contain("| B | Edit a cell and Save All | mutating (default: not set in plan) | NOT_RUN | NOT_RUN | - | - | 0 | NOT_RUN |"));
    }

    [Test]
    public void NoTimingsJson_NotMeasuredTotalAndPhaseRows()
    {
        File.Delete(Path.Combine(_pageDir, "timings.json"));
        var md = Build();
        Assert.That(md, Does.Contain("Total lifecycle time: not measured (no lifecycle recorded)"));
        Assert.That(md, Does.Contain("| Phase 1 (Plan) | 04:00 | not-measured | not-measured |"));
        Assert.That(md, Does.Contain("| Phase 5 (Report) | 01:00 | not-measured | not-measured |"));
        Assert.That(md, Does.Contain("| Total | 15:00 | not measured | not-measured |"));
        Assert.That(md, Does.Contain("Phase timing not measured (no lifecycle recorded)"));
    }

    [Test]
    public void EmptyLifecycles_NotMeasured_AndMissingFixesAndStatusTolerated()
    {
        File.WriteAllText(Path.Combine(_pageDir, "timings.json"), "{ \"schemaVersion\": 2, \"latest\": {}, \"runs\": [], \"lifecycles\": [] }");
        File.Delete(Path.Combine(_pageDir, "fixes.json"));
        File.Delete(Path.Combine(_pageDir, "status.json"));
        var md = Build();
        Assert.That(md, Does.Contain("Total lifecycle time: not measured (no lifecycle recorded)"));
        Assert.That(md, Does.Contain("fixes applied 0"));
        var fixes = md[md.IndexOf("### Bug Fixes", StringComparison.Ordinal)..md.IndexOf("### Manual Tests", StringComparison.Ordinal)];
        Assert.That(fixes, Does.Contain("| None found. |"));
    }

    [Test]
    public void WithLifecycle_PhaseTableAndTotals()
    {
        var md = Build();
        Assert.That(md, Does.Contain("Total lifecycle time: 11:30 of 15:00 target (gate wait excluded: 02:30) - WITHIN TARGET."));
        Assert.That(md, Does.Contain("| Phase 3 (Run Reference + Target) | 04:00 | 03:00 | done |"));
        Assert.That(md, Does.Contain("| Total | 15:00 | 11:30 | WITHIN TARGET |"));
    }

    [Test]
    public void PhaseOverBudget_FlaggedInTableAndLimitations()
    {
        var tm = ReadJson("timings.json");
        tm["lifecycles"]![0]!["phases"]![4]!["segments"] = new JsonArray(new JsonObject
        {
            ["start"] = "2026-10-27T10:13:00.0000000+05:30", ["end"] = "2026-10-27T10:14:30.0000000+05:30"
        });
        tm["lifecycles"]![0]!["finishedAt"] = "2026-10-27T10:15:00.0000000+05:30";
        WriteJson("timings.json", tm);
        var md = Build();
        Assert.That(md, Does.Contain("| Phase 5 (Report) | 01:00 | 01:30 | done, OVER |"));
        Assert.That(md, Does.Contain("- Phase 5 (Report) over budget: 01:30 of 01:00"));
    }
}

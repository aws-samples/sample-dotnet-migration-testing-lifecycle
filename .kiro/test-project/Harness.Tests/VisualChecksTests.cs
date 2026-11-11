using System.Text.Json.Nodes;
using MigrationTests.Playwright;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>
/// Browser-free tests for the geometry diff (layout.* rows from css-map roles), the content sanity checks
/// (PageHealth.ScanText, Target Functional Gaps, Reference defects) and how both surface in the reports.
/// </summary>
[TestFixture]
public class VisualChecksTests
{
    private const string Progress = "Phase 5 of 5 (Report) - elapsed 11:30 - estimated remaining 00:00 - waiting for you";
    private string _pageDir = "";

    [SetUp]
    public void SetUp()
    {
        _pageDir = Path.Combine(Path.GetTempPath(), "vc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pageDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_pageDir)) Directory.Delete(_pageDir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    // ─────────────────────────────── builders ───────────────────────────────

    private static RunStep Step(string name, params ContentIssue[] issues) => new()
    {
        Step = name, Title = name, Description = name, ActionMs = 100, StepMs = 120, Status = "PASS",
        Assertions = new() { new RunAssertion { Field = "x", Type = "EQUALS", Expected = "1", Actual = "1", ExpectedIsConstant = true, Passed = true, Message = "x ok" } },
        ContentIssues = issues.ToList()
    };

    private static RunResultsFile Results(string system, params RunStep[] steps) => new()
    {
        Page = "PayorList", Module = "A", System = system, Test = system + "_Test", RunId = "r1",
        Steps = steps.ToList(), CssStatesCaptured = new() { "page_load" },
        Totals = new RunTotals
        {
            StepsPass = steps.Length, AssertionsTotal = steps.Length, AssertionsPassed = steps.Length
        }
    };

    private static ContentIssue Leak(string excerpt, params string[] tokens) =>
        new() { Kind = ContentIssue.LeakedSyntax, Excerpt = excerpt, Tokens = tokens.ToList() };

    private static ContentIssue ConsoleErr(string msg) => new() { Kind = ContentIssue.ConsoleError, Excerpt = msg };

    private static Dictionary<string, string> Box(double y, double height, double? lastBottom = null)
    {
        var d = new Dictionary<string, string>
        {
            ["layout.x"] = "10", ["layout.y"] = y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["layout.width"] = "800", ["layout.height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["layout.visibleCount"] = "1"
        };
        if (lastBottom != null) d["layout.lastBottom"] = lastBottom.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return d;
    }

    private static CssMapFile RoleMap(Dictionary<string, string>? rowAccepted = null) => new()
    {
        Elements = new()
        {
            new CssMapElement { Name = "Grid data row", State = "page_load", Role = "row", Reference = "tr.jqgrow", Target = "tr.mud-table-row", Accepted = rowAccepted ?? new() },
            new CssMapElement { Name = "Grid", State = "page_load", Role = "grid", Reference = "#gview", Target = ".mud-table-container" },
            new CssMapElement { Name = "Grid header row", State = "page_load", Role = "header", Reference = "tr.labels", Target = ".mud-table-head tr" },
            new CssMapElement { Name = "Grid pager", State = "page_load", Role = "pager", Reference = "#pager", Target = ".mud-table-pagination" },
            new CssMapElement { Name = "Filter panel", State = "page_load", Role = "filter", Reference = ".filter-panel", Target = ".mud-paper" }
        }
    };

    /// <summary>Writes the map, a one-state plan, both mapped captures and both results files, then generates Module A.</summary>
    private ModuleSummary RunGeometry(CssMapFile map, Dictionary<string, Dictionary<string, string>> refCss,
        Dictionary<string, Dictionary<string, string>> tgtCss, VisualChecksSettings? vc = null)
    {
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "css-map.json"), map);
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "plan.json"), new PlanFile
        {
            Page = "PayorList", Revision = 1, Modules = new() { new PlanModule { Id = "A", CssStates = new() { "page_load" } } }
        });
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Reference", "css", "css_mapped_page_load.json"), refCss);
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Target", "css", "css_mapped_page_load.json"), tgtCss);
        return Run(Results("Reference", Step("Step1")), Results("Target", Step("Step1")), vc ?? new VisualChecksSettings());
    }

    private ModuleSummary Run(RunResultsFile reference, RunResultsFile target, VisualChecksSettings? vc = null)
    {
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Reference", "results.json"), reference);
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Target", "results.json"), target);
        ModuleReportGenerator.Generate(_pageDir, "A", vc ?? new VisualChecksSettings());
        return ReportJson.Read<ModuleSummary>(Path.Combine(_pageDir, "runs", "ModuleA", "summary.json"))!;
    }

    /// <summary>Same layout on both sides except the overrides; row 100..130 (height 30), grid at y 80, pager right below.</summary>
    private static Dictionary<string, Dictionary<string, string>> Layout(double rowHeight = 30, double gridHeight = 400,
        double headerHeight = 25, double gapToPager = 2, double gapToGrid = 15)
    {
        const double gridY = 100, filterY = 0;
        var rowTop = gridY + headerHeight;
        var lastBottom = gridY + gridHeight;
        return new()
        {
            ["Grid data row"] = Box(rowTop, rowHeight, lastBottom),
            ["Grid"] = Box(gridY, gridHeight),
            ["Grid header row"] = Box(gridY, headerHeight),
            ["Grid pager"] = Box(lastBottom + gapToPager, 30),
            ["Filter panel"] = Box(filterY, gridY - gapToGrid - filterY)
        };
    }

    // ─────────────────────────────── geometry severity ───────────────────────────────

    [TestCase(100, 103, null, TestName = "Geometry_3px_WithinTolerance")]
    [TestCase(100, 106, null, TestName = "Geometry_6pxOn100_Is6Percent_WithinTolerance")]
    [TestCase(20, 26, "MEDIUM", TestName = "Geometry_20vs26_Medium")]
    [TestCase(23, 38, "HIGH", TestName = "Geometry_23vs38_Over50Percent_High")]
    [TestCase(400, 445, "HIGH", TestName = "Geometry_Over40px_High")]
    [TestCase(400, 440, null, TestName = "Geometry_Exactly40pxAnd10Percent_WithinTolerance")]
    [TestCase(300, 340, "MEDIUM", TestName = "Geometry_40pxAnd13Percent_Medium")]
    [TestCase(300, 341, "HIGH", TestName = "Geometry_41px_High")]
    [TestCase(2, 300, "HIGH", TestName = "Geometry_GapToPager_2vs300_High")]
    [TestCase(0, 4, null, TestName = "Geometry_ZeroReference_4px_WithinTolerance")]
    public void GeometrySeverity_Thresholds(double r, double t, string? expected) =>
        Assert.That(ModuleReportGenerator.GeometrySeverity(r, t), Is.EqualTo(expected));

    [Test]
    public void GeometrySeverity_UsesConfiguredTolerance()
    {
        var tol = new GeometryTolerance { MinDiffPx = 1, MinDiffPct = 1, HighDiffPx = 5, HighDiffPct = 90 };
        Assert.That(ModuleReportGenerator.GeometrySeverity(100, 103, tol), Is.EqualTo("MEDIUM"));
        Assert.That(ModuleReportGenerator.GeometrySeverity(100, 106, tol), Is.EqualTo("HIGH"));
        Assert.That(ModuleReportGenerator.GeometrySeverity(100, 106, new GeometryTolerance { MinDiffPx = 10 }), Is.Null);
    }

    // ─────────────────────────────── geometry rows ───────────────────────────────

    [Test]
    public void Geometry_WithinTolerance_NoRows()
    {
        var s = RunGeometry(RoleMap(), Layout(), Layout(rowHeight: 33, gridHeight: 406, headerHeight: 27, gapToPager: 5, gapToGrid: 18));
        Assert.That(s.CssDiffs.Where(d => d.Property.StartsWith("layout.")), Is.Empty, string.Join("; ", s.CssDiffs.Select(d => $"{d.Element}.{d.Property}")));
        Assert.That(s.ActionableCssDiffs, Is.EqualTo(0));
        Assert.That(s.TargetStatus, Is.EqualTo("PASS"));
    }

    [Test]
    public void Geometry_GapToPager_High_ActionableAndInCssDiffTable()
    {
        var s = RunGeometry(RoleMap(), Layout(), Layout(gapToPager: 300));
        var gap = s.CssDiffs.Single(d => d.Property == "layout.gapToPager");
        Assert.That(gap.Element, Is.EqualTo("Grid pager"));
        Assert.That((gap.Reference, gap.Target, gap.Severity, gap.Actionable), Is.EqualTo(("2px", "300px", "HIGH", true)));
        Assert.That(s.ActionableCssDiffs, Is.EqualTo(1));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
        var md = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_CSS_Diff.md"));
        Assert.That(md, Does.Contain("| Grid pager [page_load] | layout.gapToPager | 2px | 300px | HIGH | Fix Target layout: layout.gapToPager 300px vs Reference 2px (diff +298px, 14900%) |"));
    }

    [Test]
    public void Geometry_RowHeight_High_ThenAccepted_NotActionable()
    {
        var s = RunGeometry(RoleMap(), Layout(rowHeight: 23), Layout(rowHeight: 38));
        var row = s.CssDiffs.Single(d => d.Property == "layout.rowHeight");
        Assert.That((row.Element, row.Severity, row.Actionable, row.Accepted), Is.EqualTo(("Grid data row", "HIGH", true, false)));

        var accepted = RunGeometry(RoleMap(new() { ["layout.rowHeight"] = "MudBlazor 38px cells agreed" }), Layout(rowHeight: 23), Layout(rowHeight: 38));
        var arow = accepted.CssDiffs.Single(d => d.Property == "layout.rowHeight");
        Assert.That((arow.Accepted, arow.Actionable, arow.AcceptedReason), Is.EqualTo((true, false, "MudBlazor 38px cells agreed")));
        Assert.That(accepted.ActionableCssDiffs, Is.EqualTo(0));
        Assert.That(accepted.TargetStatus, Is.EqualTo("PASS"));
        var md = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_CSS_Diff.md"));
        Assert.That(md, Does.Contain("- The row height is bigger on the new page (Target) by 15px (38px vs 23px on the old page (Reference)). Accepted by css-map.json: MudBlazor 38px cells agreed"));
        Assert.That(md, Does.Contain("Technical detail: Grid data row [page_load] layout.rowHeight, Reference `23px` vs Target `38px`."));
    }

    [Test]
    public void Geometry_AcceptingRowHeight_DoesNotAcceptTheGap()
    {
        var s = RunGeometry(RoleMap(new() { ["layout.rowHeight"] = "agreed" }), Layout(rowHeight: 23), Layout(rowHeight: 38, gapToPager: 300));
        Assert.That(s.CssDiffs.Single(d => d.Property == "layout.rowHeight").Actionable, Is.False);
        Assert.That(s.CssDiffs.Single(d => d.Property == "layout.gapToPager").Actionable, Is.True);
        Assert.That(s.ActionableCssDiffs, Is.EqualTo(1));
    }

    [Test]
    public void Geometry_MediumRows_ForGridHeaderAndFilterGap()
    {
        var s = RunGeometry(RoleMap(), Layout(gridHeight: 300, headerHeight: 20, gapToGrid: 20),
            Layout(gridHeight: 335, headerHeight: 26, gapToGrid: 26));
        string Sev(string prop) => s.CssDiffs.Single(d => d.Property == prop).Severity;
        Assert.That(Sev("layout.height"), Is.EqualTo("MEDIUM"));       // 300 vs 335: +35px, 11.7%
        Assert.That(Sev("layout.headerHeight"), Is.EqualTo("MEDIUM")); // 20 vs 26: +6px, 30%
        Assert.That(Sev("layout.gapToGrid"), Is.EqualTo("MEDIUM"));    // 20 vs 26: +6px, 30%
        Assert.That(s.CssDiffs.Any(d => d.Property is "layout.rowHeight" or "layout.gapToPager"), Is.False, "unchanged metrics stay quiet");
        Assert.That(s.CssDiffs.Single(d => d.Property == "layout.headerHeight").Element, Is.EqualTo("Grid header row"));
        Assert.That(s.CssDiffs.Single(d => d.Property == "layout.gapToGrid").Element, Is.EqualTo("Filter panel"));
    }

    [Test]
    public void Geometry_OldCaptureWithoutLayoutKeys_NoGeometryRows()
    {
        var plain = new Dictionary<string, Dictionary<string, string>>
        {
            ["Grid data row"] = new() { ["height"] = "23px" }, ["Grid"] = new(), ["Grid header row"] = new(),
            ["Grid pager"] = new(), ["Filter panel"] = new()
        };
        var tgt = new Dictionary<string, Dictionary<string, string>>
        {
            ["Grid data row"] = new() { ["height"] = "23px" }, ["Grid"] = new(), ["Grid header row"] = new(),
            ["Grid pager"] = new(), ["Filter panel"] = new()
        };
        var s = RunGeometry(RoleMap(), plain, tgt);
        Assert.That(s.CssDiffs, Is.Empty);
    }

    [Test]
    public void Geometry_RawLayoutKeys_ComparedOnlyWhenListed()
    {
        var map = new CssMapFile
        {
            Elements = new()
            {
                new CssMapElement { Name = "Search button", State = "page_load", Reference = "#b", Target = "button", Properties = new() { ["layout.width"] = "" } },
                new CssMapElement { Name = "Clear button", State = "page_load", Reference = "#c", Target = "button" }
            }
        };
        var r = new Dictionary<string, Dictionary<string, string>> { ["Search button"] = Box(0, 30), ["Clear button"] = Box(0, 30) };
        var t = new Dictionary<string, Dictionary<string, string>> { ["Search button"] = Box(0, 60), ["Clear button"] = Box(0, 60) };
        r["Search button"]["layout.width"] = "80"; t["Search button"]["layout.width"] = "120";
        r["Clear button"]["layout.width"] = "80"; t["Clear button"]["layout.width"] = "120";
        var s = RunGeometry(map, r, t);
        var row = s.CssDiffs.Single();
        Assert.That((row.Element, row.Property, row.Reference, row.Target, row.Severity), Is.EqualTo(("Search button", "layout.width", "80px", "120px", "MEDIUM")));
    }

    [Test]
    public void Geometry_MissingRoleElementOnTarget_ReportedAsMissingElement_NotAsGeometry()
    {
        var tgt = Layout();
        tgt["Grid pager"] = new() { ["__missing"] = "true" };
        var s = RunGeometry(RoleMap(), Layout(), tgt);
        Assert.That(s.CssDiffs.Single().Property, Is.EqualTo("(element)"));
        Assert.That(s.CssDiffs.Single().Element, Is.EqualTo("Grid pager"));
    }

    [Test]
    public void Geometry_ReferenceLargerGap_StillCounted_ActionSaysConfirmNotCopy()
    {
        var s = RunGeometry(RoleMap(), Layout(gapToPager: 300), Layout());
        var gap = s.CssDiffs.Single(d => d.Property == "layout.gapToPager");
        Assert.That((gap.Reference, gap.Target, gap.Severity, gap.Actionable), Is.EqualTo(("300px", "2px", "HIGH", true)));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"), "a Reference-side gap is never a silent PASS");
        var md = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_CSS_Diff.md"));
        Assert.That(md, Does.Contain("| Grid pager [page_load] | layout.gapToPager | 300px | 2px | HIGH | Reference larger: layout.gapToPager Reference 300px vs Target 2px (diff -298px, 99%)."));
        Assert.That(md, Does.Contain("never add empty space or height to the Target to match"));
        Assert.That(md, Does.Not.Contain("Fix Target layout: layout.gapToPager"));
    }

    [Test]
    public void Geometry_GapInsideTallPagerContainer_UsesContentTop_High()
    {
        // PayorList run: jqGrid #payorPager box starts right under the last row (gap 0) but is 1040px tall,
        // with the pager widget at its bottom. The gap is measured to the pager's visible content.
        var r = Layout();
        r["Grid pager"]["layout.y"] = "500";
        r["Grid pager"]["layout.height"] = "1040";
        r["Grid pager"]["layout.contentTop"] = "1515";
        var t = Layout();
        t["Grid pager"]["layout.contentTop"] = "510";
        var s = RunGeometry(RoleMap(), r, t);
        var gap = s.CssDiffs.Single(d => d.Property == "layout.gapToPager");
        Assert.That((gap.Reference, gap.Target, gap.Severity, gap.Actionable), Is.EqualTo(("1015px", "10px", "HIGH", true)));
    }

    [Test]
    public void Geometry_ReferenceRoleMiss_CountedUnmeasuredRow()
    {
        var r = Layout();
        r["Grid pager"] = new() { ["__missing"] = "true" };
        var s = RunGeometry(RoleMap(), r, Layout(gapToPager: 300));
        var row = s.CssDiffs.Single();
        Assert.That((row.Element, row.Property, row.Reference, row.Target, row.Severity, row.Actionable),
            Is.EqualTo(("Grid pager", "(unmeasured)", "missing", "present", "MEDIUM", true)));
        Assert.That(s.ActionableCssDiffs, Is.EqualTo(1));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
        var md = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_CSS_Diff.md"));
        Assert.That(md, Does.Contain("| Grid pager [page_load] | (unmeasured) | missing | present | MEDIUM | Geometry not measured: the Reference selector matched nothing"));
    }

    [Test]
    public void Geometry_ReferenceRowMiss_BlocksGapToPager_CountedOnRowElement()
    {
        var r = Layout();
        r["Grid data row"] = new() { ["__missing"] = "true" };
        var t = Layout(gapToPager: 300);
        t["Grid data row"] = new() { ["__missing"] = "true" };
        var s = RunGeometry(RoleMap(), r, t);
        var row = s.CssDiffs.Single();
        Assert.That((row.Element, row.Property, row.Reference, row.Target), Is.EqualTo(("Grid data row", "(unmeasured)", "missing", "missing")));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
    }

    [Test]
    public void Geometry_PagerWithoutRowRole_CountedUnmeasuredRow()
    {
        var map = RoleMap();
        map.Elements.RemoveAll(e => e.Role == "row");
        var r = Layout(); r.Remove("Grid data row");
        var t = Layout(); t.Remove("Grid data row");
        var s = RunGeometry(map, r, t);
        var row = s.CssDiffs.Single();
        Assert.That((row.Element, row.Property, row.Severity, row.Actionable), Is.EqualTo(("Grid pager", "(unmeasured: needs role row)", "MEDIUM", true)));
        var md = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_CSS_Diff.md"));
        Assert.That(md, Does.Contain("Geometry not measured: add a state-map/css-map.json element with role 'row' in this state"));
    }

    // ─────────────────────────────── content scan ───────────────────────────────

    [Test]
    public void ScanText_DirectiveLine_OneIssueWithAllTokens()
    {
        var text = "<%@ Page Language=\"C#\" AutoEventWireup=\"true\" CodeBehind=\"PayorList.aspx.cs\" Inherits=\"PayorContracts.Legacy.Pages.PayorList\" %>\n" +
                   "Payor Information Management\nPayor Name:";
        var issues = PageHealth.ScanText(text);
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Kind, Is.EqualTo(ContentIssue.LeakedSyntax));
        Assert.That(issues[0].Tokens, Is.EqualTo(new[] { "<%@", "<%", "%>" }));
        Assert.That(issues[0].Excerpt, Does.StartWith("<%@ Page Language=\"C#\""));
        Assert.That(issues[0].Excerpt.Length, Is.LessThanOrEqualTo(PageHealth.ExcerptMax + 6));
    }

    [Test]
    public void ScanText_StandaloneTokens_MatchWholeTokensOnly()
    {
        Assert.That(PageHealth.ScanText("Total: NaN").Single().Tokens, Is.EqualTo(new[] { "NaN" }));
        Assert.That(PageHealth.ScanText("Name: undefined").Single().Tokens, Is.EqualTo(new[] { "undefined" }));
        Assert.That(PageHealth.ScanText("Value [object Object] shown").Single().Tokens, Is.EqualTo(new[] { "[object Object]" }));
        Assert.That(PageHealth.ScanText("(undefined)").Single().Tokens, Is.EqualTo(new[] { "undefined" }));
        Assert.That(PageHealth.ScanText("Nancy Smith\nundefinedX\nxundefined\nBANANA\nNaNa\n[NaN]"), Is.Empty);
    }

    [Test]
    public void ScanText_TemplateTokens_AndCleanPage()
    {
        Assert.That(PageHealth.ScanText("Hello {{ user.name }}").Single().Tokens, Is.EqualTo(new[] { "{{", "}}" }));
        Assert.That(PageHealth.ScanText("@page \"/payors\"").Single().Tokens, Is.EqualTo(new[] { "@page" }));
        Assert.That(PageHealth.ScanText("@code { int x; }").Single().Tokens, Is.EqualTo(new[] { "@code" }));
        Assert.That(PageHealth.ScanText("Payor Information Management\nView 1 - 10 of 15\n50% complete"), Is.Empty);
        Assert.That(PageHealth.ScanText("same <% line\nsame <% line"), Has.Count.EqualTo(1), "one issue per distinct line");
    }

    [Test]
    public void ScanText_CustomTokenListsFromSettings()
    {
        var vc = new VisualChecksSettings { LeakedTokens = new[] { "${" }, StandaloneTokens = new[] { "null" } };
        Assert.That(PageHealth.ScanText("<%@ Page %>", vc), Is.Empty, "default tokens replaced");
        Assert.That(PageHealth.ScanText("Price ${amount}", vc).Single().Tokens, Is.EqualTo(new[] { "${" }));
        Assert.That(PageHealth.ScanText("Owner: null", vc).Single().Tokens, Is.EqualTo(new[] { "null" }));
        Assert.That(PageHealth.ScanText("nullable", vc), Is.Empty);
    }

    [Test]
    public void ScanText_LongLine_ExcerptAroundTheMatch()
    {
        var line = new string('a', 300) + " <%= Eval(\"Name\") %> " + new string('b', 300);
        var e = PageHealth.ScanText(line).Single().Excerpt;
        Assert.That(e, Does.Contain("<%="));
        Assert.That(e, Does.StartWith("...").And.EndWith("..."));
    }

    [Test]
    public void VisualChecksSettings_LoadMissingFile_Defaults_AndBlockOverrides()
    {
        var d = VisualChecksSettings.Load(Path.Combine(_pageDir, "nope.json"));
        Assert.That(d.LeakedTokens, Is.EqualTo(VisualChecksSettings.DefaultLeakedTokens));
        Assert.That(d.StandaloneTokens, Is.EqualTo(VisualChecksSettings.DefaultStandaloneTokens));
        Assert.That((d.Geometry.MinDiffPx, d.Geometry.MinDiffPct, d.Geometry.HighDiffPx, d.Geometry.HighDiffPct), Is.EqualTo((4d, 10d, 40d, 50d)));
        var path = Path.Combine(_pageDir, "s.json");
        File.WriteAllText(path, "{ \"VisualChecks\": { \"LeakedTokens\": [\"<?php\"], \"Geometry\": { \"MinDiffPx\": 8 } } }");
        var o = VisualChecksSettings.Load(path);
        Assert.That(o.LeakedTokens, Is.EqualTo(new[] { "<?php" }));
        Assert.That(o.StandaloneTokens, Is.EqualTo(VisualChecksSettings.DefaultStandaloneTokens));
        Assert.That((o.Geometry.MinDiffPx, o.Geometry.HighDiffPx), Is.EqualTo((8d, 40d)));
    }

    [Test]
    public void Runner_ContentIssuesAndConsoleErrors_LandOnTheirStep_StatusUnchanged()
    {
        PageHealth.Drain();
        try
        {
            var runner = new ModuleTestRunner("PayorList", "A", "Reference", 0);
            PageHealth.Add(ContentIssue.ConsoleError, "boom\nsecond line");
            runner.StepPass("NAV", "Opened the page");
            runner.AddContentIssues("Step1", PageHealth.ScanText("<%@ Page %>\nok"));
            runner.StepPass("Step1", "Loaded");
            PageHealth.Add(ContentIssue.PageError, "late error");
            var r = runner.BuildResults(aborted: false, abortReason: null);
            Assert.That(r.Steps.Select(s => s.Status), Is.EqualTo(new[] { "PASS", "PASS" }));
            Assert.That(r.Steps[0].ContentIssues.Single().Excerpt, Is.EqualTo("boom second line"));
            Assert.That(r.Steps[1].ContentIssues.Select(i => i.Kind), Is.EqualTo(new[] { ContentIssue.LeakedSyntax, ContentIssue.PageError }));
        }
        finally
        {
            ModuleTestRunner.Current = null;
            PageHealth.Drain();
        }
    }

    // ─────────────────────────────── content classification ───────────────────────────────

    [Test]
    public void Content_TargetLeakedSyntaxOnTwoSteps_OneHighAppFinding_TargetFails()
    {
        var leak = Leak("{{ payor.name }}", "{{", "}}");
        var s = Run(Results("Reference", Step("Step1"), Step("Step2")), Results("Target", Step("Step1", leak), Step("Step2", leak)));
        var f = s.Findings.Single();
        Assert.That((f.Id, f.Kind, f.Severity, f.Step, f.Field), Is.EqualTo(("A-Step1-page.leakedSyntax", "app", "HIGH", "Step1", "page.leakedSyntax")));
        Assert.That(f.Target, Is.EqualTo("{{ payor.name }}"));
        Assert.That(f.Reference, Is.EqualTo("(none on Reference)"));
        Assert.That(f.Sources, Is.EqualTo(new[] { "content-scan" }));
        Assert.That(f.Details, Is.EqualTo(new[] { "Step1, Step2: \"{{ payor.name }}\" (tokens: {{, }})" }));
        Assert.That(s.OpenFindings, Is.EqualTo(1));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
        Assert.That(s.ReferenceStatus, Is.EqualTo("PASS"));
        Assert.That(s.ReferenceDefectCount, Is.EqualTo(0));
    }

    [Test]
    public void Content_ReferenceLeakOnly_ReferenceDefect_DoesNotFailTargetParity()
    {
        var directive = Leak("<%@ Page Language=\"C#\" AutoEventWireup=\"true\" %>", "<%@", "<%", "%>");
        var s = Run(Results("Reference", Step("Step1", directive), Step("Step2", directive)), Results("Target", Step("Step1"), Step("Step2")));
        Assert.That(s.ReferenceDefectCount, Is.EqualTo(1));
        var r = s.ReferenceDefects.Single();
        Assert.That((r.Id, r.Step, r.Kind, r.Severity), Is.EqualTo(("A-REF1", "Step1", "leaked-syntax", "HIGH")));
        Assert.That(r.Steps, Is.EqualTo(new[] { "Step1", "Step2" }));
        Assert.That(s.OpenFindings, Is.EqualTo(0));
        Assert.That(s.Findings, Is.Empty);
        Assert.That(s.TargetStatus, Is.EqualTo("PASS"), "a Reference defect never fails Target parity");
        Assert.That(s.ReferenceStatus, Is.EqualTo("PASS"), "Reference verdict unchanged");

        var parity = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_Parity.md"));
        Assert.That(parity, Does.Contain("## Reference defects (baseline issues, not counted in Target parity)"));
        Assert.That(parity, Does.Contain("| A-REF1 | HIGH | Step1, Step2 | Leaked server/template syntax | <%@ Page Language=\"C#\" AutoEventWireup=\"true\" %> (tokens: <%@, <%, %>) |"));
        Assert.That(parity, Does.Contain("- Reference defects: 1 "));
    }

    [Test]
    public void Content_TargetConsoleError_MediumFinding_AndPageErrorSeparateField()
    {
        var s = Run(Results("Reference", Step("Step1")),
            Results("Target", Step("Step1", ConsoleErr("Failed to load resource: 404")), Step("Step2", new ContentIssue { Kind = ContentIssue.PageError, Excerpt = "TypeError: x is undefined" })));
        var console = s.Findings.Single(f => f.Field == "page.consoleError");
        Assert.That((console.Severity, console.Kind, console.Id), Is.EqualTo(("MEDIUM", "app", "A-Step1-page.consoleError")));
        var page = s.Findings.Single(f => f.Field == "page.pageError");
        Assert.That((page.Severity, page.Step), Is.EqualTo(("MEDIUM", "Step2")));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
    }

    [Test]
    public void Content_SameLeakOnBoth_TargetFindingNamesTheReferenceDefect()
    {
        var leak = Leak("<% Response.Write(x) %>", "<%", "%>");
        var s = Run(Results("Reference", Step("Step1", leak)), Results("Target", Step("Step1", leak)));
        Assert.That(s.ReferenceDefectCount, Is.EqualTo(1));
        Assert.That(s.Findings.Single().Reference, Is.EqualTo("also on Reference (Reference defect): <% Response.Write(x) %>"));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
    }

    // ─────────────────────────────── chat + final report ───────────────────────────────

    private static void AddReferenceDefect(string pageDir)
    {
        var path = Path.Combine(pageDir, "runs", "ModuleA", "summary.json");
        var s = JsonNode.Parse(File.ReadAllText(path))!;
        s["referenceDefectCount"] = 1;
        s["referenceDefects"] = new JsonArray(new JsonObject
        {
            ["id"] = "A-REF1", ["step"] = "Step1", ["kind"] = "leaked-syntax", ["severity"] = "HIGH",
            ["tokens"] = new JsonArray("<%@", "%>"), ["excerpt"] = "<%@ Page Language=\"C#\" %>", ["steps"] = new JsonArray("Step1", "Step2")
        });
        File.WriteAllText(path, s.ToJsonString());
    }

    [Test]
    public void ChatModuleResult_ReferenceDefectsSection_OnlyWhenPresent()
    {
        var dir = ChatRendererTests.CopyFixture();
        try
        {
            var (before, _) = ChatRenderer.RenderParts(dir, "module-result", "A", Progress, ChatRendererTests.Now);
            Assert.That(before[0].Markdown, Does.Not.Contain("Reference Defects"));
            Assert.That(before[0].Markdown, Does.Not.Contain("Reference defects"));

            AddReferenceDefect(dir);
            var (parts, summary) = ChatRenderer.RenderParts(dir, "module-result", "A", Progress, ChatRendererTests.Now);
            var md = parts[0].Markdown;
            Assert.That(md, Does.Contain("#### Reference Defects"));
            Assert.That(md, Does.Contain("| ID | Severity | Step | Text |"));
            Assert.That(md, Does.Contain("| A-REF1 | HIGH | Step1, Step2 | Server or template code is shown as page text on the old page: <%@ Page Language=\"C#\" %> (tokens: <%@, %>) |"));
            Assert.That(md, Does.Contain("they do not fail Target parity"));
            Assert.That(summary, Does.Contain("; 1 Reference defects;"));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
        }
    }

    [Test]
    public void FinalReport_ReferenceIssuesSection_ListsDefect_AndLimitationsLine()
    {
        var dir = ChatRendererTests.CopyFixture();
        try
        {
            var clean = File.ReadAllText(FinalReportBuilder.WriteFromDisk(dir, ChatRendererTests.Now));
            Assert.That(clean, Does.Contain("### Reference Issues"));
            var cleanSection = clean[clean.IndexOf("### Reference Issues", StringComparison.Ordinal)..];
            Assert.That(cleanSection, Does.Contain("| None found. |"));

            AddReferenceDefect(dir);
            var md = File.ReadAllText(FinalReportBuilder.WriteFromDisk(dir, ChatRendererTests.Now));
            Assert.That(md, Does.Contain("| Module | ID | Severity | Step | Kind | Text |"));
            Assert.That(md, Does.Contain("| A | A-REF1 | HIGH | Step1, Step2 | Leaked server/template syntax | <%@ Page Language=\"C#\" %> (tokens: <%@, %>) |"));
            Assert.That(md, Does.Contain("- 1 Reference defect(s) found on the baseline (see Reference Issues); they do not fail Target parity"));
            Assert.That(md, Does.Contain("Overall: FAIL"), "fixture verdict unchanged by the defect");
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
        }
    }
}

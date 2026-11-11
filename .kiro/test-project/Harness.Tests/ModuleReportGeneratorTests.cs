using System.Text.Json;
using MigrationTests.Playwright;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>
/// Browser-free unit tests for ModuleReportGenerator (design §12).
/// Run: dotnet test --no-build --filter "FullyQualifiedName~MigrationTests.Reporting.ModuleReportGeneratorTests"
/// </summary>
[TestFixture]
public class ModuleReportGeneratorTests
{
    private string _pageDir = "";

    [SetUp]
    public void SetUp()
    {
        _pageDir = Path.Combine(Path.GetTempPath(), "mrg-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pageDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_pageDir)) Directory.Delete(_pageDir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
    }

    // ─────────────────────────────── builders ───────────────────────────────

    private static RunAssertion Eq(string field, string expected, string actual) => new()
    {
        Field = field, Type = "EQUALS", Expected = expected, Actual = actual, ExpectedIsConstant = true,
        Passed = expected == actual, Message = $"{field}: \"{actual}\" (expected \"{expected}\")"
    };

    private static RunAssertion Changes(string field, string before, string after) => new()
    {
        Field = field, Type = "CHANGES", Expected = before, Actual = after, ExpectedIsConstant = false,
        Passed = before != after, Message = $"{field}: \"{before}\" -> \"{after}\""
    };

    private static RunAssertion Same(string field, string before, string after) => new()
    {
        Field = field, Type = "STAYS SAME", Expected = before, Actual = after, ExpectedIsConstant = false,
        Passed = before == after, Message = $"{field}: \"{before}\" (unchanged)"
    };

    private static RunStep Step(string name, params RunAssertion[] assertions) => new()
    {
        Step = name, Title = name, Description = name, ActionMs = 100, StepMs = 120,
        Status = assertions.All(a => a.Passed) ? "PASS" : "FAIL", Assertions = assertions.ToList()
    };

    private static RunCapture Cap(string step, string state, string field, string value) =>
        new() { Step = step, State = state, Field = field, Value = value };

    private static RunResultsFile Results(string system, IEnumerable<RunStep> steps, IEnumerable<RunCapture>? captures = null,
        IEnumerable<string>? cssStates = null)
    {
        var s = steps.ToList();
        var all = s.SelectMany(x => x.Assertions).ToList();
        return new RunResultsFile
        {
            Page = "PayorList", Module = "A", System = system, Test = system + "_Test", RunId = "r1",
            Steps = s, Captures = captures?.ToList() ?? new(), CssStatesCaptured = cssStates?.ToList() ?? new(),
            Totals = new RunTotals
            {
                StepsPass = s.Count(x => x.Status == "PASS"), StepsFail = s.Count(x => x.Status == "FAIL"),
                AssertionsTotal = all.Count, AssertionsPassed = all.Count(a => a.Passed), AssertionsFailed = all.Count(a => !a.Passed)
            }
        };
    }

    private void Write(RunResultsFile r) =>
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", r.System, "results.json"), r);

    private ModuleSummary Run(RunResultsFile reference, RunResultsFile target)
    {
        Write(reference);
        Write(target);
        ModuleReportGenerator.Generate(_pageDir, "A");
        var summary = ReportJson.Read<ModuleSummary>(Path.Combine(_pageDir, "runs", "ModuleA", "summary.json"));
        Assert.That(summary, Is.Not.Null, "summary.json must be written");
        return summary!;
    }

    private static Finding Single(ModuleSummary s, string step, string field)
    {
        var matches = s.Findings.Where(f => f.Step == step && f.Field == field).ToList();
        Assert.That(matches, Has.Count.EqualTo(1), $"exactly one finding for {step}/{field}");
        return matches[0];
    }

    // ─────────────────────────────── severity ───────────────────────────────

    [Test]
    public void GapSeverity_DigitsDiffer_IsHigh()
    {
        Assert.That(ModuleReportGenerator.GapSeverity("View 1 - 10 of 15", "1-5 of 15"), Is.EqualTo("HIGH"));
        Assert.That(ModuleReportGenerator.GapSeverity("asc", "desc"), Is.EqualTo("HIGH"));
    }

    [Test]
    public void GapSeverity_SameDigitsDifferentText_IsMedium() =>
        Assert.That(ModuleReportGenerator.GapSeverity("View 11 - 15 of 15", "11-15 of 15"), Is.EqualTo("MEDIUM"));

    [Test]
    public void GapSeverity_WhitespaceCaseComma_IsLow()
    {
        Assert.That(ModuleReportGenerator.GapSeverity("1,404", "1404"), Is.EqualTo("LOW"));
        Assert.That(ModuleReportGenerator.GapSeverity("Active  Payor", "active payor"), Is.EqualTo("LOW"));
        Assert.That(ModuleReportGenerator.GapSeverity(" same ", "same"), Is.Null);
    }

    [Test]
    public void CssSeverity_DefaultsAndNoise()
    {
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("color"), Is.EqualTo("HIGH"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("background-color"), Is.EqualTo("HIGH"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("font-weight"), Is.EqualTo("HIGH"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("display"), Is.EqualTo("HIGH"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("font-size"), Is.EqualTo("MEDIUM"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("padding-left"), Is.EqualTo("MEDIUM"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("margin"), Is.EqualTo("MEDIUM"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("border-bottom"), Is.EqualTo("MEDIUM"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("line-height"), Is.EqualTo("MEDIUM"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("font-family"), Is.EqualTo("LOW"));
        Assert.That(ModuleReportGenerator.DefaultCssSeverity("text-align"), Is.EqualTo("LOW"));

        Assert.That(CssCompareHelper.IsNoiseDiff("width", "100px", "101px"), Is.True);
        Assert.That(CssCompareHelper.IsNoiseDiff("text-align", "start", "left"), Is.True);
        Assert.That(CssCompareHelper.IsNoiseDiff("font-weight", "normal", "400"), Is.True);
        Assert.That(CssCompareHelper.IsNoiseDiff("color", "rgb(0, 0, 0)", "RGB(0, 0, 0)"), Is.True);
        Assert.That(CssCompareHelper.IsNoiseDiff("width", "100px", "110px"), Is.False);
    }

    [Test]
    public void Css_MappedDiffs_SeverityNoiseAcceptedAndActionableCount()
    {
        var map = new CssMapFile
        {
            Elements = new()
            {
                new CssMapElement
                {
                    Name = "Header", State = "page_load", Reference = "th", Target = "th",
                    Properties = new() { ["background-color"] = "MEDIUM", ["font-weight"] = "LOW", ["text-align"] = "LOW", ["width"] = "MEDIUM", ["padding"] = "" },
                    Accepted = new() { ["font-weight"] = "Material 500 vs 600, agreed acceptable" }
                },
                new CssMapElement { Name = "Pager", State = "page_load", Reference = ".pager", Target = ".mud-pager", Properties = new() { ["color"] = "" } }
            }
        };
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "css-map.json"), map);
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "plan.json"), new PlanFile
        {
            Page = "PayorList", Revision = 1,
            Modules = new() { new PlanModule { Id = "A", CssStates = new() { "page_load", "sorted" } } }
        });
        var refCss = new Dictionary<string, Dictionary<string, string>>
        {
            ["Header"] = new() { ["background-color"] = "rgb(1, 2, 3)", ["font-weight"] = "500", ["text-align"] = "start", ["width"] = "100px", ["padding"] = "4px" },
            ["Pager"] = new() { ["color"] = "rgb(0, 0, 0)" }
        };
        var tgtCss = new Dictionary<string, Dictionary<string, string>>
        {
            ["Header"] = new() { ["background-color"] = "rgb(9, 9, 9)", ["font-weight"] = "600", ["text-align"] = "left", ["width"] = "101px", ["padding"] = "8px" },
            ["Pager"] = new() { ["__missing"] = "true" }
        };
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Reference", "css", "css_mapped_page_load.json"), refCss);
        ReportJson.Write(Path.Combine(_pageDir, "runs", "ModuleA", "Target", "css", "css_mapped_page_load.json"), tgtCss);

        var r = Results("Reference", new[] { Step("Step1", Eq("x", "1", "1")) }, cssStates: new[] { "page_load", "sorted" });
        var t = Results("Target", new[] { Step("Step1", Eq("x", "1", "1")) }, cssStates: new[] { "page_load" });
        var s = Run(r, t);

        CssDiffEntry D(string el, string prop) => s.CssDiffs.Single(d => d.Element == el && d.Property == prop);
        Assert.That(D("Header", "background-color").Severity, Is.EqualTo("MEDIUM"), "map severity overrides HIGH default");
        Assert.That(D("Header", "font-weight").Accepted, Is.True);
        Assert.That(D("Header", "font-weight").Actionable, Is.False);
        Assert.That(D("Header", "padding").Severity, Is.EqualTo("MEDIUM"), "blank map severity falls back to default");
        Assert.That(s.CssDiffs.Any(d => d.Property == "text-align"), Is.False, "start vs left is noise");
        Assert.That(s.CssDiffs.Any(d => d.Property == "width"), Is.False, "±1px is noise");
        Assert.That(D("Pager", "(element)").Severity, Is.EqualTo("MEDIUM"), "missing element is MEDIUM");
        Assert.That(s.CssDiffs.Single(d => d.State == "sorted").Property, Is.EqualTo("(not captured on Target)"));
        // background-color + padding + missing Pager + planned 'sorted' missing on Target
        Assert.That(s.ActionableCssDiffs, Is.EqualTo(4));
        Assert.That(s.CssDiffsTotal, Is.EqualTo(5));
        Assert.That(File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_CSS_Diff.md")),
            Does.Contain("Align to reference: set background-color: rgb(1, 2, 3)"));
    }

    [Test]
    public void Css_MapMissing_CountsOneUnmappedDiffPerPlannedState()
    {
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "plan.json"), new PlanFile
        {
            Modules = new() { new PlanModule { Id = "A", CssStates = new() { "page_load", "sorted" } } }
        });
        var r = Results("Reference", new[] { Step("Step1", Eq("x", "1", "1")) }, cssStates: new[] { "page_load", "sorted" });
        var t = Results("Target", new[] { Step("Step1", Eq("x", "1", "1")) }, cssStates: new[] { "page_load", "sorted" });
        t.CssMapMissing = true;
        var s = Run(r, t);
        Assert.That(s.CssDiffs.Count(d => d.Element == "(unmapped)"), Is.EqualTo(2));
        // One raw row per planned state; identical repeats across states are one page-level group.
        Assert.That(s.ActionableCssRows, Is.EqualTo(2));
        Assert.That(s.ActionableCssDiffs, Is.EqualTo(1));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
    }

    // ─────────────────────────────── classification ───────────────────────────────

    [Test]
    public void Step3PagerInfoShape_OneAppFindingMedium_WithMissingChanges()
    {
        var r = Results("Reference", new[] { Step("Step3",
            Changes("pager.info", "View 1 - 10 of 15", "View 11 - 15 of 15"),
            Eq("pager.info", "View 11 - 15 of 15", "View 11 - 15 of 15")) });
        var t = Results("Target", new[] { Step("Step3", Eq("pager.info", "View 11 - 15 of 15", "11-15 of 15")) });
        var s = Run(r, t);

        Assert.That(s.Findings, Has.Count.EqualTo(1));
        var f = Single(s, "Step3", "pager.info");
        Assert.That(f.Kind, Is.EqualTo("app"));
        Assert.That(f.Severity, Is.EqualTo("MEDIUM"));
        Assert.That(f.Id, Is.EqualTo("A-Step3-pager.info"));
        Assert.That(f.MissingAssertions.Select(m => m.Type), Is.EqualTo(new[] { "CHANGES" }));
        Assert.That(s.Findings.Any(x => x.Kind == "test-expectation"), Is.False);
        Assert.That(s.OpenFindings, Is.EqualTo(1));
    }

    [Test]
    public void Step2Shape_TwoAssertionLevelCoverageFindings()
    {
        var r = Results("Reference", new[] { Step("Step2",
            Eq("grid.columnMap", "0:ID|1:Payor Name", "0:ID|1:Payor Name"),
            Same("grid.rowCount", "10", "10"),
            Same("pager.info", "View 1 - 10 of 15", "View 1 - 10 of 15")) });
        var t = Results("Target", new[] { Step("Step2", Eq("grid.columnMap", "0:ID|1:Payor Name", "0:ID|1:Payor Name")) });
        var s = Run(r, t);

        Assert.That(s.OpenFindings, Is.EqualTo(2));
        var a = Single(s, "Step2", "grid.rowCount");
        var b = Single(s, "Step2", "pager.info");
        Assert.That(a.Kind, Is.EqualTo("test-coverage"));
        Assert.That(b.Kind, Is.EqualTo("test-coverage"));
        Assert.That(a.Severity, Is.EqualTo("MEDIUM"));
        Assert.That(a.MissingAssertions.Single().Type, Is.EqualTo("STAYS SAME"));
    }

    [Test]
    public void Step1CaptureAndFailedEquals_OneAppFinding_SourcesAssertionAndCapture()
    {
        var r = Results("Reference", new[] { Step("Step1", Eq("pager.info", "View 1 - 10 of 15", "View 1 - 10 of 15")) },
            new[] { Cap("Step1", "S0", "pager.info", "View 1 - 10 of 15") });
        var t = Results("Target", new[] { Step("Step1", Eq("pager.info", "View 1 - 10 of 15", "1-10 of 15")) },
            new[] { Cap("Step1", "S0", "pager.info", "1-10 of 15") });
        var s = Run(r, t);

        Assert.That(s.Findings, Has.Count.EqualTo(1), "failed assertion + capture mismatch on the same key dedupe to one finding");
        var f = Single(s, "Step1", "pager.info");
        Assert.That(f.Kind, Is.EqualTo("app"));
        Assert.That(f.Sources, Does.Contain("assertion"));
        Assert.That(f.Sources, Does.Contain("capture"));
    }

    [Test]
    public void Dedupe_FailedAssertionCaptureMismatchAndMissingAssertion_OneFinding()
    {
        var r = Results("Reference", new[] { Step("Step4",
                Changes("grid.row[0].id", "1", "11"), Eq("grid.row[0].id", "11", "11")) },
            new[] { Cap("Step4", "S_p2", "grid.row[0].id", "11") });
        var t = Results("Target", new[] { Step("Step4", Eq("grid.row[0].id", "11", "6")) },
            new[] { Cap("Step4", "S_p2", "grid.row[0].id", "6") });
        var s = Run(r, t);

        Assert.That(s.Findings, Has.Count.EqualTo(1));
        var f = Single(s, "Step4", "grid.row[0].id");
        Assert.That(f.Kind, Is.EqualTo("app"));
        Assert.That(f.Severity, Is.EqualTo("HIGH"));
        Assert.That(f.MissingAssertions, Has.Count.EqualTo(1));
        Assert.That(f.Sources, Is.SupersetOf(new[] { "assertion", "capture", "missing-assertion" }));
    }

    [Test]
    public void Overlap_ExpectedMismatchPlusValueMismatch_IsTestExpectation()
    {
        var r = Results("Reference", new[] { Step("Step1", Eq("grid.rowCount", "10", "10")) },
            new[] { Cap("Step1", "S0", "grid.rowCount", "10") });
        var t = Results("Target", new[] { Step("Step1", Eq("grid.rowCount", "5", "5")) },
            new[] { Cap("Step1", "S0", "grid.rowCount", "5") });
        var s = Run(r, t);

        Assert.That(s.Findings, Has.Count.EqualTo(1));
        var f = Single(s, "Step1", "grid.rowCount");
        Assert.That(f.Kind, Is.EqualTo("test-expectation"));
        Assert.That(f.Reference, Is.EqualTo("10"));
        Assert.That(f.Target, Is.EqualTo("5"));
    }

    [Test]
    public void Overlap_MissingTargetStepPlusCaptureMismatch_IsStepCoverage()
    {
        var r = Results("Reference", new[] { Step("Step1", Eq("a", "1", "1")), Step("Step4", Eq("pager.info", "x", "x")) },
            new[] { Cap("Step4", "S1", "pager.info", "View 1 - 10 of 15") });
        // Target never ran Step4 but a capture is attributed to it.
        var t = Results("Target", new[] { Step("Step1", Eq("a", "1", "1")) },
            new[] { Cap("Step4", "S1", "pager.info", "1-5 of 15") });
        var s = Run(r, t);

        Assert.That(s.Findings, Has.Count.EqualTo(1));
        var f = Single(s, "Step4", "*");
        Assert.That(f.Kind, Is.EqualTo("test-coverage"));
        Assert.That(f.Id, Is.EqualTo("A-Step4-*"));
        Assert.That(s.OpenFindings, Is.EqualTo(1));
    }

    [Test]
    public void UnmatchedTargetAssertion_ListedLow_NotCounted()
    {
        var r = Results("Reference", new[] { Step("Step7", Same("grid.row[0].id", "1", "1")) });
        var t = Results("Target", new[] { Step("Step7", Eq("grid.row[0].id", "1", "1")) });
        var s = Run(r, t);

        Assert.That(s.OpenFindings, Is.EqualTo(1), "Reference STAYS SAME unmirrored → one test-coverage");
        Assert.That(Single(s, "Step7", "grid.row[0].id").Kind, Is.EqualTo("test-coverage"));
        var u = s.Unmatched.Single();
        Assert.That(u.Kind, Is.EqualTo("unmatched-target-assertion"));
        Assert.That(u.Severity, Is.EqualTo("LOW"));
        Assert.That(s.Findings.Any(f => f.Kind == "unmatched-target-assertion"), Is.False);
    }

    [Test]
    public void EqualsVsEquals_DifferentConstants_IsTestExpectation()
    {
        var r = Results("Reference", new[] { Step("Step1", Eq("heading.text", "Payor - Info", "Payor - Info")) });
        var t = Results("Target", new[] { Step("Step1", Eq("heading.text", "Payor Info", "Payor Info")) });
        var s = Run(r, t);
        Assert.That(Single(s, "Step1", "heading.text").Kind, Is.EqualTo("test-expectation"));
    }

    [Test]
    public void ChangesVsChanges_DifferentPreState_IsNotTestExpectation()
    {
        var r = Results("Reference", new[] { Step("Step3", Changes("grid.row[0].id", "1", "11")) });
        var t = Results("Target", new[] { Step("Step3", Changes("grid.row[0].id", "6", "11")) });
        var s = Run(r, t);
        Assert.That(s.Findings.Any(f => f.Kind == "test-expectation"), Is.False);
        Assert.That(s.OpenFindings, Is.EqualTo(0), "same post-state → no difference");

        // Different post-state on a paired CHANGES → app.
        var t2 = Results("Target", new[] { Step("Step3", Changes("grid.row[0].id", "6", "7")) });
        var s2 = Run(r, t2);
        Assert.That(Single(s2, "Step3", "grid.row[0].id").Kind, Is.EqualTo("app"));
    }

    [Test]
    public void MissingTargetCapture_IsCoverageWithMissingCaptures()
    {
        var r = Results("Reference", new[] { Step("Step2", Eq("grid.columnMap", "0:ID", "0:ID")) },
            new[] { Cap("Step2", "S0", "grid.columnMap", "0:ID") });
        var t = Results("Target", new[] { Step("Step2", Eq("grid.columnMap", "0:ID", "0:ID")) });
        var s = Run(r, t);

        Assert.That(s.OpenFindings, Is.EqualTo(1));
        var f = Single(s, "Step2", "grid.columnMap");
        Assert.That(f.Kind, Is.EqualTo("test-coverage"));
        Assert.That(f.MissingCaptures, Has.Count.EqualTo(1));
        Assert.That(f.MissingCaptures[0].State, Is.EqualTo("S0"));
        Assert.That(f.MissingCaptures[0].Field, Is.EqualTo("grid.columnMap"));
    }

    [Test]
    public void TargetOnlyCapture_NoFinding_ListedLow()
    {
        var r = Results("Reference", new[] { Step("Step1", Eq("a", "1", "1")) });
        var t = Results("Target", new[] { Step("Step1", Eq("a", "1", "1")) },
            new[] { Cap("Step1", "S0", "extra.field", "x") });
        var s = Run(r, t);

        Assert.That(s.OpenFindings, Is.EqualTo(0));
        Assert.That(s.Findings, Is.Empty);
        var u = s.Unmatched.Single();
        Assert.That(u.Kind, Is.EqualTo("unmatched-target-capture"));
        Assert.That(u.Severity, Is.EqualTo("LOW"));
    }

    [Test]
    public void NavAndPreMissingCaptures_AreCoverageKeyedOnNavPre_NotAbsorbed()
    {
        var r = Results("Reference", new[] { Step("NAV"), Step("Step1", Eq("a", "1", "1")) },
            new[] { Cap("NAV", "S0", "heading.text", "Payor"), Cap("PRE", "S1", "filter.value", "Acme") });
        // Target has no NAV step record and never captures either field.
        var t = Results("Target", new[] { Step("Step1", Eq("a", "1", "1")) });
        var s = Run(r, t);

        Assert.That(s.OpenFindings, Is.EqualTo(2));
        var nav = Single(s, "NAV", "heading.text");
        var pre = Single(s, "PRE", "filter.value");
        Assert.That(nav.Kind, Is.EqualTo("test-coverage"));
        Assert.That(pre.Kind, Is.EqualTo("test-coverage"));
        Assert.That(nav.MissingCaptures.Single().State, Is.EqualTo("S0"));
        Assert.That(s.Findings.Any(f => f.Field == "*"), Is.False, "NAV/PRE are present on both systems");
    }

    [Test]
    public void BoolMapping_TrueCapitalised_IsLowerCasedAndNotADifference()
    {
        var mapped = ModuleTestRunner.MapAssertion(
            new ModuleTestRunner.AssertionResult("Step1", "grid.visible", "true", "True", "BOOL", true, "Grid is visible"));
        Assert.That(mapped.Actual, Is.EqualTo("true"));
        Assert.That(mapped.Expected, Is.EqualTo("true"));
        Assert.That(mapped.ExpectedIsConstant, Is.True);

        var eq = ModuleTestRunner.MapAssertion(
            new ModuleTestRunner.AssertionResult("Step1", "x", "10", "5", "EQUALS", false, "m"));
        Assert.That((eq.Expected, eq.Actual, eq.ExpectedIsConstant), Is.EqualTo(("10", "5", true)));
        var ch = ModuleTestRunner.MapAssertion(
            new ModuleTestRunner.AssertionResult("Step1", "x", "a", "b", "CHANGES", true, "m"));
        Assert.That((ch.Expected, ch.Actual, ch.ExpectedIsConstant), Is.EqualTo(("a", "b", false)));

        var refBool = ModuleTestRunner.MapAssertion(
            new ModuleTestRunner.AssertionResult("Step1", "grid.visible", "true", "true", "BOOL", true, "Grid is visible"));
        var s = Run(Results("Reference", new[] { Step("Step1", refBool) }), Results("Target", new[] { Step("Step1", mapped) }));
        Assert.That(s.OpenFindings, Is.EqualTo(0));
        Assert.That(s.Findings, Is.Empty);
    }

    [Test]
    public void TargetResultsMissing_IsCountedSelectorFinding_AndTargetFails()
    {
        Write(Results("Reference", new[] { Step("NAV"), Step("Step1", Eq("a", "1", "1")) }));
        ModuleReportGenerator.Generate(_pageDir, "A");
        var s = ReportJson.Read<ModuleSummary>(Path.Combine(_pageDir, "runs", "ModuleA", "summary.json"))!;
        Assert.That(s.OpenFindings, Is.GreaterThan(0));
        Assert.That(s.Findings[0].Kind, Is.EqualTo("test-selector"));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
        Assert.That(File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_Parity.md")), Does.Contain("Target results.json missing"));
    }

    [Test]
    public void CorruptInput_ThrowsReportInputException()
    {
        var p = Path.Combine(_pageDir, "runs", "ModuleA", "Reference", "results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "{ not json");
        Assert.Throws<ReportInputException>(() => ModuleReportGenerator.Generate(_pageDir, "A"));
    }

    // ─────────────────────────────── outputs ───────────────────────────────

    [Test]
    public void SummaryJson_WrittenWithExpectedCounts_AndFourReports()
    {
        ReportJson.Write(Path.Combine(_pageDir, "state-map", "plan.json"), new PlanFile
        {
            Page = "PayorList", Revision = 1,
            Modules = new() { new PlanModule { Id = "A", NotTested = new() { new PlanNotTested { Item = "Print", Reason = "No printer" } } } }
        });
        var r = Results("Reference", new[]
        {
            Step("NAV"),
            Step("Step1", Eq("grid.rowCount", "10", "10")),
            Step("Step2", Same("pager.info", "View 1 - 10 of 15", "View 1 - 10 of 15")),
            Step("Step3", Eq("x", "1", "1")),
        });
        r.Steps.Add(new RunStep { Step = "Step4", Status = "NOT TESTED", Description = "Export | needs file system" });
        var t = Results("Target", new[]
        {
            Step("NAV"),
            Step("Step1", Eq("grid.rowCount", "10", "5")),
            Step("Step2"),
        });
        var s = Run(r, t);

        // Step1 app, Step2/pager.info coverage, Step3 missing step (row 1). Reference NOT TESTED Step4 is not a gap.
        Assert.That(s.OpenFindings, Is.EqualTo(3));
        Assert.That(s.Findings.Any(f => f.Step == "Step4"), Is.False);
        Assert.That(Single(s, "Step1", "grid.rowCount").Kind, Is.EqualTo("app"));
        Assert.That(Single(s, "Step3", "*").Kind, Is.EqualTo("test-coverage"));
        Assert.That(s.NotTested, Is.EqualTo(2));
        Assert.That(s.ReferenceStatus, Is.EqualTo("PASS"));
        Assert.That(s.TargetStatus, Is.EqualTo("FAIL"));
        Assert.That(s.ActionableCssDiffs, Is.EqualTo(0));

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_pageDir, "runs", "ModuleA", "summary.json")));
        foreach (var prop in new[] { "openFindings", "actionableCssDiffs", "cssDiffsTotal", "notTested", "findings", "cssDiffs" })
            Assert.That(doc.RootElement.TryGetProperty(prop, out _), Is.True, $"summary.json has {prop}");
        var f0 = doc.RootElement.GetProperty("findings")[0];
        foreach (var prop in new[] { "id", "kind", "step", "field", "reference", "target", "expected", "severity", "actionable", "sources", "missingAssertions", "missingCaptures" })
            Assert.That(f0.TryGetProperty(prop, out _), Is.True, $"finding has {prop}");

        foreach (var name in new[] { "ModuleA_Parity.md", "ModuleA_CSS_Diff.md", "ModuleA_Performance.md", "ModuleA_ManualTests.md" })
            Assert.That(File.Exists(Path.Combine(_pageDir, "reports", name)), Is.True, name);
        var manual = File.ReadAllText(Path.Combine(_pageDir, "reports", "ModuleA_ManualTests.md"));
        Assert.That(manual, Does.Contain("Print"));
        Assert.That(manual, Does.Contain("needs file system"));
    }

    [Test]
    public void AllMode_WritesFinalReport()
    {
        // No timings.json / status.json in this page dir: exercises the "No lifecycle" path.
        Write(Results("Reference", new[] { Step("Step1", Eq("a", "1", "1")) }));
        Write(Results("Target", new[] { Step("Step1", Eq("a", "1", "2")) }));
        ReportJson.Write(Path.Combine(_pageDir, "fixes.json"), new List<FixEntry>
        {
            new() { Round = 1, Source = "loop", Module = "A", FindingIds = new() { "A-Step1-a" }, Kind = "app", File = "PayorList.razor", Change = "fix", Before = "2", After = "1" }
        });
        var all = ModuleReportGenerator.Generate(_pageDir, "ALL");
        Assert.That(all.Select(m => m.Module), Is.EqualTo(new[] { "A" }));
        var finalPath = Path.Combine(_pageDir, "reports", "FinalReport.md");
        Assert.That(File.Exists(finalPath), Is.True, "FinalReport.md must be written by Generate(ALL)");
        var report = File.ReadAllText(finalPath);
        Assert.That(report, Does.Contain("### Module Results"));
        Assert.That(report, Does.Contain("A-Step1-a"));
        Assert.That(File.Exists(Path.Combine(_pageDir, "reports", "FinalReport.data.md")), Is.False,
            "FinalReport.data.md is no longer produced");
    }
}

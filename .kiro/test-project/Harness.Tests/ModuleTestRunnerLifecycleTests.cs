using System.Text.Json;
using MigrationTests.Playwright;
using NUnit.Framework;

namespace MigrationTests.Reporting;

/// <summary>
/// Browser-free checks of ModuleTestRunner lifecycle output (results.json, JSONL events, capture
/// step context). ExecuteStep is driven with a null page: CaptureGridHtml swallows the error and
/// returns "", so no browser is needed.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ModuleTestRunnerLifecycleTests
{
    private static readonly string[] EnvNames = { "LIFECYCLE_PAGE_DIR", "LIFECYCLE_RUN_ID", "LIFECYCLE_EVENTS_FILE" };
    private readonly Dictionary<string, string?> _savedEnv = new();
    private string _pageDir = "";
    private const string Module = "ZZ"; // keeps snapshots/<System>/ModuleZZ separate from real modules

    [SetUp]
    public void SetUp()
    {
        foreach (var n in EnvNames) _savedEnv[n] = Environment.GetEnvironmentVariable(n);
        _pageDir = Path.Combine(Path.GetTempPath(), "mtr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pageDir);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var (n, v) in _savedEnv) Environment.SetEnvironmentVariable(n, v);
        ModuleTestRunner.Current = null;
        try { if (Directory.Exists(_pageDir)) Directory.Delete(_pageDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
        foreach (var sys in new[] { "Reference", "Target" })
        {
            var snap = Path.Combine(AppContext.BaseDirectory, "snapshots", sys, $"Module{Module}");
            try { if (Directory.Exists(snap)) Directory.Delete(snap, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TestContext.Progress.WriteLine($"Teardown cleanup skipped: {ex.Message}"); }
        }
    }

    private string EventsFile => Path.Combine(_pageDir, "runs", "events-r42.jsonl");

    private void SetLifecycleEnv()
    {
        Environment.SetEnvironmentVariable("LIFECYCLE_PAGE_DIR", _pageDir);
        Environment.SetEnvironmentVariable("LIFECYCLE_RUN_ID", "r42");
        Environment.SetEnvironmentVariable("LIFECYCLE_EVENTS_FILE", EventsFile);
    }

    private RunResultsFile ReadResults(string system) =>
        ReportJson.Read<RunResultsFile>(Path.Combine(_pageDir, "runs", $"Module{Module}", system, "results.json"))
        ?? throw new AssertionException("results.json not written");

    [Test]
    public async Task LifecycleMode_WritesResultsJsonAndEvents()
    {
        SetLifecycleEnv();
        var runner = new ModuleTestRunner("PayorList", Module, "Target", 1);
        Assert.That(ModuleTestRunner.Current, Is.SameAs(runner));

        runner.Capture("S0", "heading.text", "Payor");                // before any step → NAV
        runner.StepPass("NAV", "Opened /Pages/PayorList");
        await runner.ExecuteStep("Step1", null!, "Click Next page (1 -> 2)",
            action: async () => await Task.Delay(30),
            assertions: async () =>
            {
                runner.Capture("S_p2", "pager.info", "11-15 of 15");
                runner.AssertChanged("Step1", "pager.info", "1-10 of 15", "11-15 of 15");
                runner.AssertEquals("Step1", "grid.rowCount", "5", "5");
                runner.Assert("Step1", "grid.visible", true, "Grid is visible", "Grid NOT visible");
                await Task.CompletedTask;
            });
        runner.Capture("S_p2", "filter.value", "");                  // between steps → PRE
        await runner.GenerateReport();

        var r = ReadResults("Target");
        Assert.That(r.SchemaVersion, Is.EqualTo(1));
        Assert.That((r.Page, r.Module, r.System, r.RunId, r.Aborted), Is.EqualTo(("PayorList", Module, "Target", "r42", false)));
        Assert.That(r.Test, Is.EqualTo(nameof(LifecycleMode_WritesResultsJsonAndEvents)));
        Assert.That(r.Totals.StepsPass, Is.EqualTo(2));
        Assert.That(r.Totals.AssertionsTotal, Is.EqualTo(3));
        Assert.That(r.Steps.Select(s => s.Step), Is.EqualTo(new[] { "NAV", "Step1" }));

        var step1 = r.Steps[1];
        Assert.That(step1.Status, Is.EqualTo("PASS"));
        Assert.That(step1.Title, Is.EqualTo("Click Next page (1 -> 2)"));
        Assert.That(step1.ActionMs, Is.GreaterThanOrEqualTo(20));
        Assert.That(step1.StepMs, Is.GreaterThanOrEqualTo(step1.ActionMs));
        Assert.That(step1.Description, Does.Contain("CHANGED: pager.info: \"1-10 of 15\" -> \"11-15 of 15\""));
        Assert.That(step1.Description, Does.Contain("VERIFIED: grid.rowCount: \"5\" = expected"));
        Assert.That(step1.Assertions.Select(a => a.Type), Is.EqualTo(new[] { "CHANGES", "EQUALS", "BOOL" }));
        Assert.That(step1.Assertions[2].Actual, Is.EqualTo("true"), "BOOL actual lower-cased");

        Assert.That(r.Captures.Select(c => (c.Step, c.State, c.Field)), Is.EqualTo(new[]
        {
            ("NAV", "S0", "heading.text"), ("Step1", "S_p2", "pager.info"), ("PRE", "S_p2", "filter.value")
        }));

        var lines = File.ReadAllLines(EventsFile).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.That(lines.Select(e => $"{e.GetProperty("event").GetString()}:{e.GetProperty("step").GetString()}"),
            Is.EqualTo(new[] { "step-end:NAV", "step-start:Step1", "step-end:Step1" }));
        var end = lines[2];
        foreach (var p in new[] { "ts", "runId", "module", "system", "step", "status", "description", "actionMs", "stepMs", "assertionsTotal", "assertionsFailed", "detail" })
            Assert.That(end.TryGetProperty(p, out _), Is.True, $"step-end has {p}");
        Assert.That(end.GetProperty("assertionsTotal").GetInt32(), Is.EqualTo(3));
        Assert.That(end.GetProperty("detail").GetArrayLength(), Is.EqualTo(3));
        Assert.That(end.GetProperty("runId").GetString(), Is.EqualTo("r42"));

        // Baseline.txt still written.
        Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "snapshots", "Target", $"Module{Module}",
            $"Module{Module}_Target_Baseline.txt")), Is.True);

        // Already written → flush is a no-op (aborted stays false).
        runner.FlushIfNotWritten(TestContext.CurrentContext.Result);
        Assert.That(ReadResults("Target").Aborted, Is.False);
    }

    [Test]
    public void LifecycleMode_StepThrows_RecordsAbortedStep_AndFlushWritesAborted()
    {
        SetLifecycleEnv();
        var runner = new ModuleTestRunner("PayorList", Module, "Reference", 1);
        runner.StepPass("NAV", "Opened");
        Assert.ThrowsAsync<TimeoutException>(async () =>
            await runner.ExecuteStep("Step2", null!, "Sort by Name",
                action: () => throw new TimeoutException("Timeout 30000ms exceeded")));

        runner.FlushIfNotWritten(TestContext.CurrentContext.Result);
        var r = ReadResults("Reference");
        Assert.That(r.Aborted, Is.True);
        Assert.That(r.AbortReason, Is.Not.Empty);
        var s2 = r.Steps.Single(s => s.Step == "Step2");
        Assert.That(s2.Status, Is.EqualTo("FAIL"));
        Assert.That(s2.Description, Does.StartWith("ABORTED: Sort by Name"));
        Assert.That(s2.Description, Does.Contain("Timeout 30000ms exceeded"));
        Assert.That(File.ReadAllText(EventsFile), Does.Contain("\"step\":\"Step2\",\"status\":\"FAIL\""));
    }

    [TestCase("all", "PASS", 3, "Step4", "Step9", true)]
    [TestCase("none", "FAIL", 0, "Step1", "Step1", false)]
    [TestCase("failed", "FAIL", 3, "Step4", "Step9", true)]
    [TestCase("failed", "PASS", 0, "Step1", "Step9", true)]
    [TestCase("failed", "PASS", 8, "Step9", "Step9", true)]
    [TestCase("failed", "PASS", 3, "Step4", "Step9", false)]
    [TestCase("failed", "NOT TESTED", 3, "Step4", null, false)]
    public void ShouldCapture_Policy(string policy, string status, int index, string step, string? last, bool expected) =>
        Assert.That(ModuleTestRunner.ShouldCapture(policy, status, index, step, last), Is.EqualTo(expected));

    [Test]
    public async Task BuildResults_CarriesScreenshotPath()
    {
        SetLifecycleEnv();
        var runner = new ModuleTestRunner("PayorList", Module, "Target", 1);
        // Null page: the screenshot itself fails silently and the step still passes.
        await runner.ExecuteStep("Step1", null!, "noop", action: () => Task.CompletedTask);
        await runner.ExecuteStep("Step2", null!, "noop", action: () => Task.CompletedTask);
        runner.RecordScreenshot("Step2", runner.ScreenshotRelPath("Step2"));
        var r = runner.BuildResults(false, null);
        Assert.That(r.Steps[0].Screenshot, Is.Null);
        Assert.That(r.Steps[1].Status, Is.EqualTo("PASS"));
        Assert.That(r.Steps[1].Screenshot, Is.EqualTo($"runs/Module{Module}/Target/screenshots/Step2.jpg"));
    }

    [Test]
    public async Task WithoutLifecycleEnv_NoResultsOrEvents()
    {
        foreach (var n in EnvNames) Environment.SetEnvironmentVariable(n, null);
        var runner = new ModuleTestRunner("PayorList", Module, "Target", 1);
        runner.StepPass("NAV", "Opened");
        await runner.ExecuteStep("Step1", null!, "noop", action: () => Task.CompletedTask);
        await runner.GenerateReport();
        runner.FlushIfNotWritten(TestContext.CurrentContext.Result);

        Assert.That(Directory.GetFileSystemEntries(_pageDir), Is.Empty, "nothing written outside snapshots/");
        Assert.That(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "runs")), Is.False);
        Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "snapshots", "Target", $"Module{Module}",
            $"Module{Module}_Target_Baseline.txt")), Is.True, "Baseline.txt unchanged behaviour");
    }
}

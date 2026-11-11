using Microsoft.Playwright;
using MigrationTests.Reporting;
using NUnit.Framework;
using System.Text;
using System.Text.Json;

namespace MigrationTests.Playwright;

/// <summary>
/// Reusable infrastructure for state-machine module testing.
/// Every module test uses this to:
/// - Track assertions (CHANGES / STAYS SAME) with before/after evidence
/// - Capture HTML snapshots for diff safety net
/// - Generate standardized baseline reports
/// - Auto-capture CSS at every state
/// - Measure performance (state transition timing)
///
/// Lifecycle mode (set by lifecycle.ps1; all optional, absent = behaviour unchanged):
/// - LIFECYCLE_PAGE_DIR    → GenerateReport also writes runs/Module&lt;X&gt;/&lt;System&gt;/results.json
/// - LIFECYCLE_RUN_ID      → copied into events and results.json
/// - LIFECYCLE_EVENTS_FILE → one JSON line per step-start / step-end event
/// </summary>
public class ModuleTestRunner
{
    private readonly string _moduleName;
    private readonly string _system; // "Reference" or "Target"
    private readonly string _pageName;
    private readonly int _entityId;

    private readonly List<string> _log = new();
    private readonly List<FieldCapture> _captures = new();
    private readonly List<AssertionResult> _assertions = new();
    private readonly List<string> _uncoveredDiffs = new();
    private readonly List<StepResult> _stepResults = new();
    private readonly List<StepTiming> _stepTimings = new(); // parallel to _stepResults
    private readonly List<TimingResult> _timings = new();
    private int _snapshotSeq = 0;

    // Lifecycle context (read once per runner)
    private readonly string? _pageDir = Env("LIFECYCLE_PAGE_DIR");
    private readonly string _runId = Env("LIFECYCLE_RUN_ID") ?? "";
    private readonly string? _eventsFile = Env("LIFECYCLE_EVENTS_FILE");
    // P8 screenshots: LIFECYCLE_SCREENSHOTS = all | failed (failed + first + LIFECYCLE_LAST_STEP) | none; default all.
    private readonly string _screenshotPolicy = (Env("LIFECYCLE_SCREENSHOTS") ?? "all").Trim().ToLowerInvariant();
    private readonly string? _lastStep = Env("LIFECYCLE_LAST_STEP");
    private readonly Dictionary<string, string> _screenshots = new(StringComparer.Ordinal);
    // Content sanity checks per step name (leaked syntax in innerText, console/page errors); results.json steps[].contentIssues.
    private readonly Dictionary<string, List<ContentIssue>> _contentIssues = new(StringComparer.Ordinal);
    private int _executedSteps;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly System.Diagnostics.Stopwatch _sinceLastStepEnd = System.Diagnostics.Stopwatch.StartNew();
    private string _currentStep = "NAV";
    private bool _resultsWritten;

    // Set while ExecuteStep records its own result so StepPass/StepFail use measured timings.
    private (long ActionMs, long StepMs, string Title, List<AssertionResult> Assertions)? _executingStep;

    /// <summary>The runner of the test currently executing (one NUnit worker → a static is safe).</summary>
    public static ModuleTestRunner? Current { get; internal set; }

    internal string ModuleName => _moduleName;
    internal string SystemName => _system;

    /// <summary>
    /// Step that owns a page-health hit found now: the executing step, else (between steps, "PRE") the last
    /// recorded step, else NAV. Hits on a step name without a step result would be lost in results.json.
    /// </summary>
    internal string CurrentStep => _currentStep == "PRE" && _stepResults.Count > 0 ? _stepResults[^1].Step : _currentStep;

    /// <summary>Set by CssCompareHelper when state-map/css-map.json is absent in lifecycle mode.</summary>
    public bool CssMapMissing { get; set; }

    /// <summary>CSS states captured by CssCompareHelper in lifecycle mode.</summary>
    public List<string> CssStatesCaptured { get; } = new();

    public int AssertionCount => _assertions.Count;
    public int AssertionsPassed => _assertions.Count(a => a.Passed);
    public int AssertionsFailed => _assertions.Count(a => !a.Passed);
    public int StepsPassed => _stepResults.Count(s => s.Status == "PASS");
    public int StepsFailed => _stepResults.Count(s => s.Status == "FAIL");
    public int StepsNotTested => _stepResults.Count(s => s.Status == "NOT TESTED");

    /// <summary>Step = state id passed to Capture (S0, S_p2, ...); ExecStep = executing step (step name, NAV or PRE).</summary>
    public record FieldCapture(string Step, string Field, string Value, string ExecStep = "NAV");
    public record AssertionResult(string Step, string Field, string Before,
        string After, string Type, bool Passed, string Message);
    public record StepResult(string Step, string Status, string Description);
    public record TimingResult(string Step, string Action, long ElapsedMs);
    private record StepTiming(string Title, long ActionMs, long StepMs);

    public ModuleTestRunner(string pageName, string moduleName, string system, int entityId)
    {
        _pageName = pageName;
        _moduleName = moduleName;
        _system = system;
        _entityId = entityId;
        Current = this;
    }

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    // ═══════════════════════════════════════════════════════════
    // LOGGING
    // ═══════════════════════════════════════════════════════════

    public void Log(string message)
    {
        _log.Add(message);
        Console.WriteLine($"    {message}");
    }

    // ═══════════════════════════════════════════════════════════
    // FIELD CAPTURE (records baseline values)
    // ═══════════════════════════════════════════════════════════

    public void Capture(string step, string field, string value)
    {
        _captures.Add(new FieldCapture(step, field, value, _currentStep));
        Log($"[{step}] {field} = \"{value}\"");
    }

    // ═══════════════════════════════════════════════════════════
    // ASSERTIONS
    // ═══════════════════════════════════════════════════════════

    /// <summary>Assert a field CHANGED from before to after.</summary>
    public bool AssertChanged(string step, string field, string before, string after)
    {
        bool passed = before != after;
        var msg = passed
            ? $"{field}: \"{Truncate(before, 40)}\" -> \"{Truncate(after, 40)}\" (CHANGED)"
            : $"{field}: still \"{Truncate(before, 40)}\" (did NOT change)";
        _assertions.Add(new AssertionResult(step, field, before, after, "CHANGES", passed, msg));
        Log($"  [{(passed ? "OK" : "FAIL")}] {msg}");
        return passed;
    }

    /// <summary>Assert a field STAYED THE SAME.</summary>
    public bool AssertUnchanged(string step, string field, string before, string after)
    {
        bool passed = before == after;
        var msg = passed
            ? $"{field}: \"{Truncate(before, 40)}\" (unchanged)"
            : $"{field}: \"{Truncate(before, 40)}\" -> \"{Truncate(after, 40)}\" (CHANGED unexpectedly!)";
        _assertions.Add(new AssertionResult(step, field, before, after, "STAYS SAME", passed, msg));
        Log($"  [{(passed ? "OK" : "FAIL")}] {msg}");
        return passed;
    }

    /// <summary>Assert a field equals an expected value.</summary>
    public bool AssertEquals(string step, string field, string actual, string expected)
    {
        bool passed = actual == expected;
        var msg = passed
            ? $"{field}: \"{Truncate(actual, 40)}\" = expected"
            : $"{field}: \"{Truncate(actual, 40)}\" (expected \"{Truncate(expected, 40)}\")";
        _assertions.Add(new AssertionResult(step, field, expected, actual, "EQUALS", passed, msg));
        Log($"  [{(passed ? "OK" : "FAIL")}] {msg}");
        return passed;
    }

    /// <summary>Assert a field contains a substring.</summary>
    public bool AssertContains(string step, string field, string actual, string expectedSubstring)
    {
        bool passed = actual.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase);
        var msg = passed
            ? $"{field}: contains \"{expectedSubstring}\""
            : $"{field}: \"{Truncate(actual, 60)}\" does NOT contain \"{expectedSubstring}\"";
        _assertions.Add(new AssertionResult(step, field, expectedSubstring, actual, "CONTAINS", passed, msg));
        Log($"  [{(passed ? "OK" : "FAIL")}] {msg}");
        return passed;
    }

    /// <summary>Assert a boolean condition.</summary>
    public bool Assert(string step, string field, bool condition, string trueDesc, string falseDesc)
    {
        var msg = condition ? trueDesc : falseDesc;
        _assertions.Add(new AssertionResult(step, field, "true", condition.ToString(), "BOOL", condition, msg));
        Log($"  [{(condition ? "OK" : "FAIL")}] {msg}");
        return condition;
    }

    // ═══════════════════════════════════════════════════════════
    // STEP RESULTS
    // ═══════════════════════════════════════════════════════════

    public void StepPass(string step, string description) => RecordStep(step, "PASS", description);

    public void StepFail(string step, string description) => RecordStep(step, "FAIL", description);

    public void StepNotTested(string step, string description, string reason) =>
        RecordStep(step, "NOT TESTED", $"{description} | {reason}");

    private void RecordStep(string step, string status, string description)
    {
        _stepResults.Add(new StepResult(step, status, description));
        AddContentIssues(step, PageHealth.Drain()); // console/page errors since the previous step

        long actionMs, stepMs;
        string title;
        List<AssertionResult> stepAssertions;
        if (_executingStep is { } ex)
        {
            (actionMs, stepMs, title, stepAssertions) = ex;
        }
        else
        {
            // Manual step (e.g. NAV): time since the previous step ended.
            stepMs = actionMs = _sinceLastStepEnd.ElapsedMilliseconds;
            title = description;
            stepAssertions = _assertions.Where(a => a.Step == step).ToList();
        }
        _stepTimings.Add(new StepTiming(title, actionMs, stepMs));
        _sinceLastStepEnd.Restart();

        EmitEvent(new Dictionary<string, object?>
        {
            ["event"] = "step-end",
            ["ts"] = DateTimeOffset.Now.ToString("o"),
            ["runId"] = _runId,
            ["module"] = _moduleName,
            ["system"] = _system,
            ["step"] = step,
            ["status"] = status,
            ["description"] = description,
            ["actionMs"] = actionMs,
            ["stepMs"] = stepMs,
            ["assertionsTotal"] = stepAssertions.Count,
            ["assertionsFailed"] = stepAssertions.Count(a => !a.Passed),
            // Failed assertions first (stable order) so a FAIL step's headline names what failed.
            ["detail"] = stepAssertions.OrderBy(a => a.Passed).Take(3).Select(a => a.Message).ToList()
        });
    }

    // ═══════════════════════════════════════════════════════════
    // PERFORMANCE TIMING
    // ═══════════════════════════════════════════════════════════

    public System.Diagnostics.Stopwatch StartTiming() =>
        System.Diagnostics.Stopwatch.StartNew();

    public long StopTiming(string step, string action, System.Diagnostics.Stopwatch sw)
    {
        sw.Stop();
        _timings.Add(new TimingResult(step, action, sw.ElapsedMilliseconds));
        return sw.ElapsedMilliseconds;
    }

    // ═══════════════════════════════════════════════════════════
    // STEP LIFECYCLE (ExecuteStep — preferred single-call pattern)
    // ═══════════════════════════════════════════════════════════

    private int _assertionCountAtStepStart = 0;

    /// <summary>
    /// Single method encapsulating entire step lifecycle.
    /// Handles: timing, HTML capture before/after, assertions, diff, summary.
    /// </summary>
    public async Task ExecuteStep(string stepName, IPage page, string description,
        Func<Task> action, Func<Task>? assertions = null,
        string gridSelector = "table, .grid, [role='grid']")
    {
        var stepSw = System.Diagnostics.Stopwatch.StartNew();
        var stepIndex = _executedSteps++;
        _assertionCountAtStepStart = _assertions.Count;
        _currentStep = stepName;
        Log($"── {stepName}: {description} ──");
        EmitEvent(new Dictionary<string, object?>
        {
            ["event"] = "step-start",
            ["ts"] = DateTimeOffset.Now.ToString("o"),
            ["runId"] = _runId,
            ["module"] = _moduleName,
            ["system"] = _system,
            ["step"] = stepName,
            ["description"] = description
        });

        var sw = new System.Diagnostics.Stopwatch();
        try
        {
            var htmlBefore = await CaptureGridHtml(page, gridSelector);
            sw.Start();
            await action();
            sw.Stop();
            _timings.Add(new TimingResult(stepName, description, sw.ElapsedMilliseconds));

            if (assertions != null) await assertions();

            var htmlAfter = await CaptureGridHtml(page, gridSelector);
            var stepAssertions = _assertions.Skip(_assertionCountAtStepStart).ToList();
            var coveredTerms = stepAssertions
                .SelectMany(a => new[] { a.Before, a.After, a.Field })
                .Where(s => !string.IsNullOrEmpty(s) && s.Length > 3)
                .Distinct().ToList();

            if (!string.IsNullOrEmpty(htmlBefore) && !string.IsNullOrEmpty(htmlAfter))
                CompareHtml(stepName, htmlBefore, htmlAfter, coveredTerms);

            await ScanContent(page, stepName);
            _executingStep = (sw.ElapsedMilliseconds, stepSw.ElapsedMilliseconds, description, stepAssertions);
            GenerateStepSummary(stepName, description, stepAssertions, sw.ElapsedMilliseconds);
            // After the step's checks, before the next action (status known for the 'failed' policy).
            await CaptureStepScreenshot(page, stepName, stepIndex, _stepResults[^1].Status);
        }
        catch (Exception e) when (_executingStep == null)
        {
            // The step threw (timeout, selector, assertion exception): record it so events and
            // results.json show which step aborted, then rethrow (test behaviour unchanged).
            sw.Stop();
            var stepAssertions = _assertions.Skip(_assertionCountAtStepStart).ToList();
            await ScanContent(page, stepName);
            _executingStep = (sw.ElapsedMilliseconds, stepSw.ElapsedMilliseconds, description, stepAssertions);
            StepFail(stepName, $"ABORTED: {description}. {e.GetType().Name}: {Truncate(FirstLine(e.Message), 200)}");
            await CaptureStepScreenshot(page, stepName, stepIndex, "FAIL");
            throw;
        }
        finally
        {
            _executingStep = null;
            _currentStep = "PRE";
        }
    }

    /// <summary>
    /// Screenshot policy (pure): all → every step; none → no step; failed → failed steps, the first executed step
    /// and the module's last planned step (LIFECYCLE_LAST_STEP).
    /// </summary>
    internal static bool ShouldCapture(string policy, string status, int index, string step, string? lastStep) => policy switch
    {
        "none" => false,
        "failed" => status != "PASS" && status != "NOT TESTED" || index == 0 || (lastStep != null && step == lastStep),
        _ => true
    };

    /// <summary>Remembers the screenshot of a step for results.json (steps[].screenshot).</summary>
    internal void RecordScreenshot(string stepName, string relPath) => _screenshots[stepName] = relPath;

    /// <summary>runs/Module{X}/{System}/screenshots/{step}.jpg, relative to the page dir.</summary>
    internal string ScreenshotRelPath(string stepName) =>
        $"runs/Module{_moduleName}/{_system}/screenshots/{ScreenshotHelper.SanitizeForPath(stepName)}.jpg";

    /// <summary>Lifecycle mode only; best effort (5 s timeout), never fails the test.</summary>
    private async Task CaptureStepScreenshot(IPage page, string stepName, int index, string status)
    {
        if (_pageDir == null || !ShouldCapture(_screenshotPolicy, status, index, stepName, _lastStep)) return;
        try
        {
            var rel = ScreenshotRelPath(stepName);
            var path = Path.Combine(_pageDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Type = ScreenshotType.Jpeg, Quality = 70, FullPage = false, Path = path, Timeout = 5000
            });
            RecordScreenshot(stepName, rel);
        }
        catch (Exception e)
        {
            Log($"screenshot {stepName} skipped: {FirstLine(e.Message)}");
        }
    }

    /// <summary>
    /// Content sanity check of the step snapshot: scans document.body.innerText for leaked server/template
    /// syntax (VisualChecks settings). Best effort (3 s), never throws, never changes the step status.
    /// </summary>
    private async Task ScanContent(IPage page, string stepName)
    {
        try
        {
            var eval = page.EvaluateAsync<string>("() => document.body ? document.body.innerText : ''");
            if (await Task.WhenAny(eval, Task.Delay(3000)) != eval)
            {
                _ = eval.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); // observe a late failure
                Log($"content scan {stepName} skipped: timeout");
                return;
            }
            var text = await eval;
            AddContentIssues(stepName, PageHealth.ScanText(text, GlobalSetup.Settings.VisualChecks));
        }
        catch (Exception e)
        {
            Log($"content scan {stepName} skipped: {FirstLine(e.Message)}");
        }
    }

    internal void AddContentIssues(string stepName, IEnumerable<ContentIssue> issues)
    {
        foreach (var i in issues)
        {
            if (!_contentIssues.TryGetValue(stepName, out var list)) _contentIssues[stepName] = list = new();
            if (list.Any(x => x.Kind == i.Kind && x.Excerpt == i.Excerpt)) continue;
            list.Add(i);
            Log($"[content] {stepName} {i.Kind}: {Truncate(i.Excerpt, 120)}");
        }
    }

    private void GenerateStepSummary(string stepName, string description, List<AssertionResult> stepAssertions, long perfMs)
    {
        var failed = stepAssertions.Where(a => !a.Passed).ToList();
        var changed = stepAssertions.Where(a => a.Passed && a.Type == "CHANGES").ToList();
        var unchanged = stepAssertions.Where(a => a.Passed && a.Type == "STAYS SAME").ToList();
        var verified = stepAssertions.Where(a => a.Passed && a.Type is not ("CHANGES" or "STAYS SAME")).ToList();

        // Messages carry the actual values (reporting-standards "Step Description Rules").
        var parts = new List<string> { description.TrimEnd('.') };
        if (changed.Count > 0) parts.Add($"CHANGED: {string.Join("; ", changed.Select(a => a.Message))}");
        if (unchanged.Count > 0) parts.Add($"UNCHANGED: {string.Join("; ", unchanged.Select(a => a.Message))}");
        if (verified.Count > 0) parts.Add($"VERIFIED: {string.Join("; ", verified.Select(a => a.Message))}");
        if (failed.Count > 0) parts.Add($"FAILED: {string.Join("; ", failed.Select(a => a.Message))}");
        if (stepAssertions.Count == 0) parts.Add("No assertions recorded");

        var summary = string.Join(". ", parts) + $". [{perfMs}ms]";
        if (failed.Count == 0) StepPass(stepName, summary); // unchanged rule: no failed assertions = PASS
        else StepFail(stepName, summary);
    }

    // ═══════════════════════════════════════════════════════════
    // HTML DIFF SAFETY NET
    // ═══════════════════════════════════════════════════════════

    public async Task<string> CaptureGridHtml(IPage page, string selector = "table, .grid")
    {
        try
        {
            var el = page.Locator(selector).First;
            if (await el.IsVisibleAsync())
                return await el.EvaluateAsync<string>("el => el.outerHTML") ?? "";
        }
        catch { }
        return "";
    }

    public void CompareHtml(string stepName, string before, string after, List<string> coveredTerms)
    {
        if (before == after) return;
        var beforeLines = before.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 10).ToHashSet();
        var afterLines = after.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 10).ToHashSet();
        var changes = afterLines.Except(beforeLines).Concat(beforeLines.Except(afterLines)).ToList();
        if (changes.Count == 0) return;

        var uncovered = changes.Where(line =>
            !coveredTerms.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase))
            && !IsNoise(line)).Take(5).ToList();

        foreach (var u in uncovered)
            _uncoveredDiffs.Add($"{stepName}: {Truncate(u, 120)}");
    }

    private static bool IsNoise(string line) =>
        line.Contains("__VIEWSTATE") || line.Contains("__EVENTVALIDATION") || line.Length < 10;

    // ═══════════════════════════════════════════════════════════
    // SNAPSHOT (saves HTML + JSON state to files)
    // ═══════════════════════════════════════════════════════════

    public async Task SaveSnapshot(IPage page, string stateId, string stateName,
        string gridSelector = "form, main, body")
    {
        _snapshotSeq++;
        var dir = Path.Combine(AppContext.BaseDirectory, $"snapshots/{_system}/Module{_moduleName}");
        Directory.CreateDirectory(dir);
        var prefix = $"{_system}_Mod{_moduleName}_{_snapshotSeq:D3}_{stateId}_{stateName}";

        try
        {
            var html = await page.Locator(gridSelector).First.EvaluateAsync<string>("el => el.outerHTML");
            await File.WriteAllTextAsync(Path.Combine(dir, $"{prefix}.html"), html ?? "");
        }
        catch { }

        try
        {
            var state = _captures.Where(c => c.Step == stateId)
                .ToDictionary(c => c.Field, c => c.Value);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(dir, $"{prefix}.json"), json);
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════
    // REPORT GENERATION
    // ═══════════════════════════════════════════════════════════

    public async Task GenerateReport()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, $"snapshots/{_system}/Module{_moduleName}");
        Directory.CreateDirectory(dir);
        var reportPath = Path.Combine(dir, $"Module{_moduleName}_{_system}_Baseline.txt");

        var lines = new List<string>
        {
            $"# Module {_moduleName} {_system} Baseline -- {_pageName}",
            $"# Entity: {_entityId}",
            $"# Executed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"# Steps: PASS={StepsPassed} | FAIL={StepsFailed} | NOT TESTED={StepsNotTested}",
            $"# Assertions: {AssertionCount} total | {AssertionsPassed} passed | {AssertionsFailed} failed",
            $"# Uncovered HTML Diffs: {_uncoveredDiffs.Count}",
            "",
            "## Step Results", "",
            "| Step | Status | Description |",
            "|------|--------|-------------|"
        };
        foreach (var s in _stepResults)
            lines.Add($"| {s.Step} | {s.Status} | {s.Description.Replace("|", "/")} |");

        lines.AddRange(new[] { "", "## Captured Fields", "" });
        foreach (var c in _captures)
            lines.Add($"{c.Step} | {c.Field} = {c.Value}");

        lines.AddRange(new[] { "", "## Assertions Detail", "",
            "| # | Step | Field | Type | Before | After | Result |",
            "|---|------|-------|------|--------|-------|--------|" });
        int n = 0;
        foreach (var a in _assertions)
            lines.Add($"| {++n} | {a.Step} | {a.Field} | {a.Type} | {Truncate(a.Before, 40)} | {Truncate(a.After, 40)} | {(a.Passed ? "OK" : "FAIL")} |");

        if (_uncoveredDiffs.Count > 0)
        {
            lines.AddRange(new[] { "", "## UNCOVERED HTML DIFFS", "" });
            foreach (var d in _uncoveredDiffs) lines.Add($"  GAP: {d}");
        }

        if (_timings.Count > 0)
        {
            lines.AddRange(new[] { "", "## Performance", "",
                "| Step | Action | Time (ms) |", "|------|--------|-----------|" });
            foreach (var t in _timings) lines.Add($"| {t.Step} | {t.Action} | {t.ElapsedMs} |");
        }

        await File.WriteAllTextAsync(reportPath, string.Join("\n", lines));
        Log($"Report saved: {reportPath}");

        WriteResultsJson(aborted: false, abortReason: null);
    }

    /// <summary>
    /// Called from TestBase.TearDownAsync. If the test ended without GenerateReport (exception,
    /// timeout, early return), writes results.json with aborted=true so the runner has a verdict.
    /// </summary>
    public void FlushIfNotWritten(TestContext.ResultAdapter result)
    {
        if (_resultsWritten || _pageDir == null) return;
        string? msg = null;
        try { msg = result?.Message; } catch { }
        WriteResultsJson(aborted: true,
            abortReason: string.IsNullOrWhiteSpace(msg) ? "test ended without GenerateReport" : FirstLine(msg));
    }

    /// <summary>Maps an AssertionResult to the results.json shape (design §5.4 type table).</summary>
    internal static RunAssertion MapAssertion(AssertionResult a)
    {
        var isConst = a.Type is "EQUALS" or "CONTAINS" or "BOOL";
        return new RunAssertion
        {
            Field = a.Field,
            Type = a.Type,
            Expected = a.Type == "BOOL" ? "true" : a.Before ?? "",
            ExpectedIsConstant = isConst,
            Actual = a.Type == "BOOL" ? (a.After ?? "").ToLowerInvariant() : a.After ?? "",
            Passed = a.Passed,
            Message = a.Message
        };
    }

    internal RunResultsFile BuildResults(bool aborted, string? abortReason)
    {
        string testName = "";
        try { testName = TestContext.CurrentContext.Test.Name; } catch { }

        var file = new RunResultsFile
        {
            Page = _pageName,
            Module = _moduleName,
            System = _system,
            Test = testName,
            RunId = _runId,
            StartedAt = _startedAt.ToString("o"),
            FinishedAt = DateTimeOffset.Now.ToString("o"),
            Aborted = aborted,
            AbortReason = abortReason,
            CssMapMissing = CssMapMissing,
            Totals = new RunTotals
            {
                StepsPass = StepsPassed,
                StepsFail = StepsFailed,
                StepsNotTested = StepsNotTested,
                AssertionsTotal = AssertionCount,
                AssertionsPassed = AssertionsPassed,
                AssertionsFailed = AssertionsFailed
            },
            UncoveredHtmlDiffs = _uncoveredDiffs.ToList(),
            CssStatesCaptured = CssStatesCaptured.Distinct().ToList()
        };

        // Assertions grouped by AssertionResult.Step onto the first step entry with that name.
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < _stepResults.Count; i++)
        {
            var s = _stepResults[i];
            var t = i < _stepTimings.Count ? _stepTimings[i] : new StepTiming(s.Description, 0, 0);
            var step = new RunStep
            {
                Step = s.Step, Status = s.Status, Description = s.Description, Title = t.Title,
                ActionMs = t.ActionMs, StepMs = t.StepMs,
                Screenshot = _screenshots.TryGetValue(s.Step, out var shot) ? shot : null
            };
            if (assigned.Add(s.Step))
            {
                step.Assertions = _assertions.Where(a => a.Step == s.Step).Select(MapAssertion).ToList();
                if (_contentIssues.TryGetValue(s.Step, out var issues)) step.ContentIssues = issues.ToList();
            }
            file.Steps.Add(step);
        }
        // Console/page errors after the last recorded step belong to that step.
        var late = PageHealth.Drain();
        if (late.Count > 0 && file.Steps.Count > 0)
        {
            var last = file.Steps.First(x => x.Step == file.Steps[^1].Step);
            foreach (var i in late)
                if (!last.ContentIssues.Any(x => x.Kind == i.Kind && x.Excerpt == i.Excerpt)) last.ContentIssues.Add(i);
        }
        // Assertions recorded under a step name that never produced a step result.
        foreach (var g in _assertions.Where(a => !assigned.Contains(a.Step)).GroupBy(a => a.Step))
            file.Steps.Add(new RunStep
            {
                Step = g.Key,
                Status = g.Any(a => !a.Passed) ? "FAIL" : "PASS",
                Description = "(assertions recorded outside a step) " + string.Join("; ", g.Select(a => a.Message)),
                Title = "(no step result)",
                Assertions = g.Select(MapAssertion).ToList()
            });

        file.Captures = _captures.Select(c => new RunCapture
        {
            Step = c.ExecStep, State = c.Step, Field = c.Field, Value = c.Value
        }).ToList();
        return file;
    }

    private void WriteResultsJson(bool aborted, string? abortReason)
    {
        if (_pageDir == null) return;
        try
        {
            var path = Path.Combine(_pageDir, "runs", $"Module{_moduleName}", _system, "results.json");
            ReportJson.Write(path, BuildResults(aborted, abortReason));
            _resultsWritten = true;
            Log($"results.json saved: {path}");
        }
        catch (Exception e)
        {
            Log($"results.json write failed: {e.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EVENTS (JSONL, tailed by lifecycle.ps1)
    // ═══════════════════════════════════════════════════════════

    private void EmitEvent(Dictionary<string, object?> evt)
    {
        if (_eventsFile == null) return;
        try
        {
            var dir = Path.GetDirectoryName(_eventsFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(evt) + "\n");
            using var fs = new FileStream(_eventsFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush();
        }
        catch { /* events are best-effort; never fail a test */ }
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOfAny(new[] { '\r', '\n' });
        return i < 0 ? s : s[..i];
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";
}

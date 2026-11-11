using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.Json;

namespace MigrationTests.Playwright;

/// <summary>
/// Abstract base class for all Playwright test classes.
/// Provides per-test page, screenshot helper, and 3-state result tracking.
/// 
/// Set environment variable APP_TEST_MODE=validate for fast validation:
/// - Skips screenshots
/// - Tests that mark IssueFound will FAIL with Assert.Fail
/// 
/// Set APP_TEST_MODE=report for documentation mode:
/// - Headed browser with SlowMo
/// - All screenshots captured
/// </summary>
public abstract class TestBase
{
    private IPage? _page;
    private string _testStatus = "NeedsReview";
    private string _statusNote = "";

    private static HashSet<string>? _suppressedTests;
    private static readonly object _suppressionLock = new();

    protected static bool IsValidationMode =>
        string.Equals(Environment.GetEnvironmentVariable("APP_TEST_MODE"), "validate", StringComparison.OrdinalIgnoreCase);

    protected static bool IsReportMode =>
        string.Equals(Environment.GetEnvironmentVariable("APP_TEST_MODE"), "report", StringComparison.OrdinalIgnoreCase);

    protected IPage Page =>
        _page ?? throw new InvalidOperationException("Page is not initialized.");

    protected TestSettings Settings => GlobalSetup.Settings;

    protected void MarkVerified() { _testStatus = "Verified"; _statusNote = ""; }
    protected void MarkNeedsReview(string note)
    {
        _testStatus = "NeedsReview"; _statusNote = note;
        if (IsValidationMode) TestContext.WriteLine($"  ⚠ NEEDS REVIEW: {note}");
    }
    protected void MarkIssueFound(string note)
    {
        _testStatus = "IssueFound"; _statusNote = note;
        if (IsValidationMode) Assert.Fail($"ISSUE FOUND: {note}");
    }

    /// <summary>
    /// Captures diagnostic HTML when a selector fails.
    /// Output includes: test name, failed selector, URL, visible dialogs, main content.
    /// </summary>
    protected async Task CaptureDiagnosticHtmlAsync(string failedSelector, string context = "")
    {
        if (_page == null) return;
        try
        {
            var diagDir = Path.Combine(AppContext.BaseDirectory, "diagnostics");
            Directory.CreateDirectory(diagDir);
            var fileName = $"{TestContext.CurrentContext.Test.Name}_{DateTime.Now:HHmmss}.html";

            var html = await _page.EvaluateAsync<string>(@"(args) => {
                const lines = [];
                lines.push(`<!-- Failed Selector: ${args.selector} -->`);
                lines.push(`<!-- URL: ${window.location.href} -->`);
                const dialog = document.querySelector('[role=""dialog""], .modal');
                if (dialog) { lines.push('<!-- DIALOG -->'); lines.push(dialog.outerHTML); }
                const main = document.querySelector('main, [role=""main""], .content, body');
                lines.push(main ? main.innerHTML.substring(0, 5000) : document.body.innerHTML.substring(0, 5000));
                return lines.join('\n');
            }", new { selector = failedSelector });

            await File.WriteAllTextAsync(Path.Combine(diagDir, fileName), html ?? "");
        }
        catch { }
    }

    /// <summary>
    /// Checks if page URL indicates an auth redirect. Fails test if detected.
    /// Override the URL patterns in your app's configuration.
    /// </summary>
    protected async Task AssertNotRedirectedToAuth()
    {
        var url = Page.Url;
        var authPatterns = Settings.AuthRedirectPatterns ?? new[] { "/login", "/oauth2/", "/signin" };
        if (authPatterns.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Fail($"SESSION EXPIRED: Redirected to auth ({url}). Re-authenticate and restart.");
        }
        await Task.CompletedTask;
    }

    [SetUp]
    public async Task SetUpAsync()
    {
        _page = await GlobalSetup.AuthenticatedContext.NewPageAsync();
        if (IsValidationMode) _page.SetDefaultTimeout(30000);
        // Console errors / unhandled page errors are drained into each recorded step (content sanity checks).
        PageHealth.Attach(_page, Settings.VisualChecks);
        _testStatus = "NeedsReview";
        _statusNote = "";
        LoadSuppressions();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        // Lifecycle mode: an aborted test still writes results.json (aborted=true). No-op otherwise.
        try
        {
            ModuleTestRunner.Current?.FlushIfNotWritten(TestContext.CurrentContext.Result);
        }
        catch { }
        finally
        {
            ModuleTestRunner.Current = null;
        }

        if (_page is not null)
        {
            var testName = TestContext.CurrentContext.Test.Name;
            if (IsSuppressed(testName) && _testStatus != "Verified")
            {
                _testStatus = "Verified";
                _statusNote = "Suppressed — known limitation";
            }
            if (IsValidationMode && _testStatus == "NeedsReview" && !string.IsNullOrEmpty(_statusNote))
            {
                await CaptureDiagnosticHtmlAsync("unknown", _statusNote);
            }
            TestResultCollector.AddResult(new TestCaseResult
            {
                TestName = testName,
                Status = _testStatus,
                StatusNote = _statusNote,
                ExecutedAt = DateTime.Now
            });
            await _page.CloseAsync();
            _page = null;
        }
    }

    private static void LoadSuppressions()
    {
        lock (_suppressionLock)
        {
            if (_suppressedTests != null) return;
            _suppressedTests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "suppressions.json");
                if (!File.Exists(path)) path = Path.Combine(Directory.GetCurrentDirectory(), "suppressions.json");
                if (!File.Exists(path)) return;
                var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("suppressions", out var arr))
                    foreach (var item in arr.EnumerateArray())
                    {
                        var name = item.GetProperty("testName").GetString();
                        if (!string.IsNullOrEmpty(name)) _suppressedTests.Add(name);
                    }
            }
            catch { }
        }
    }

    private static bool IsSuppressed(string testName) =>
        _suppressedTests?.Contains(testName) == true;
}

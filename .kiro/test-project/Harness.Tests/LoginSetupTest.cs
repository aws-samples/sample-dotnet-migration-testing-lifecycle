using Microsoft.Playwright;
using NUnit.Framework;

namespace MigrationTests.Playwright.Tests;

/// <summary>
/// Authentication setup test. Opens a browser for the user to complete
/// interactive authentication (OAuth/OIDC/SSO). Saves the resulting
/// session state for reuse by all subsequent tests.
/// 
/// This test is run during pre-flight (lifecycle.ps1 -Action preflight, Step 6).
/// It opens a HEADED browser (regardless of APP_TEST_MODE) so the user
/// can interact with the login form.
/// 
/// Run manually: dotnet test --filter "FullyQualifiedName~LoginSetupTest" --no-build
/// </summary>
[TestFixture]
public class LoginSetupTest
{
    [Test]
    public async Task SaveAuthState()
    {
        // Always run headed — user needs to see and interact with login
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 300
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1400, Height = 900 },
            IgnoreHTTPSErrors = true
        });

        var page = await context.NewPageAsync();

        // Navigate to target app — this should trigger auth redirect
        var settings = GlobalSetup.Settings;
        var targetUrl = settings.BaseUrl;
        TestContext.Progress.WriteLine($"Navigating to: {targetUrl}");
        TestContext.Progress.WriteLine("Please complete authentication in the browser window...");

        await page.GotoAsync(targetUrl);

        // Wait for user to complete authentication and land back on the app
        // Timeout: 120 seconds (2 minutes) for human to type credentials
        try
        {
            await page.WaitForURLAsync(
                url => url.StartsWith(targetUrl, StringComparison.OrdinalIgnoreCase)
                    || url.Contains("/Default", StringComparison.OrdinalIgnoreCase)
                    || (!url.Contains("/login") && !url.Contains("/oauth2/") && !url.Contains("/signin")),
                new PageWaitForURLOptions { Timeout = 120000 });
        }
        catch (TimeoutException)
        {
            TestContext.Progress.WriteLine("Timeout waiting for auth completion. Saving state anyway...");
        }

        // Give the app a moment to set all cookies/tokens
        await page.WaitForTimeoutAsync(3000);

        // Save the browser storage state (cookies + localStorage)
        // Relative paths resolve against the test-project root so AuthHelper finds the same file.
        var statePath = HarnessPaths.Resolve(settings.AuthStateFilePath);

        var stateDir = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(stateDir))
            Directory.CreateDirectory(stateDir);

        await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = statePath });

        TestContext.Progress.WriteLine($"Auth state saved: {statePath}");
        TestContext.Progress.WriteLine($"File size: {new FileInfo(statePath).Length} bytes");

        Assert.That(File.Exists(statePath), "Auth state file should exist after save");
        Assert.That(new FileInfo(statePath).Length, Is.GreaterThan(100),
            "Auth state file should contain meaningful data");
    }
}

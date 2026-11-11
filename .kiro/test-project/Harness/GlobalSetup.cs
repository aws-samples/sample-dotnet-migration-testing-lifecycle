using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.Json;

namespace MigrationTests.Playwright;

/// <summary>
/// Assembly-level setup: installs browsers, launches Playwright, loads settings,
/// restores auth state, and closes the browser on teardown. Reports are written by the
/// Reports tool (lifecycle.ps1 -Action report), not here.
/// Invoked by Shared/ModuleSetUpFixture.cs, which is linked into each test project.
/// </summary>
public class GlobalSetup
{
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private static IBrowserContext? _context;

    public static TestSettings Settings { get; private set; } = new();
    public static IBrowserContext AuthenticatedContext =>
        _context ?? throw new InvalidOperationException("Browser context not initialized.");

    private static bool IsValidationMode =>
        string.Equals(Environment.GetEnvironmentVariable("APP_TEST_MODE"), "validate", StringComparison.OrdinalIgnoreCase);

    private static bool IsReportMode =>
        string.Equals(Environment.GetEnvironmentVariable("APP_TEST_MODE"), "report", StringComparison.OrdinalIgnoreCase);

    public static async Task SetUpAsync()
    {
        // Load settings
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.test.json");
        if (!File.Exists(settingsPath))
            settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.test.json");

        if (File.Exists(settingsPath))
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            Settings = JsonSerializer.Deserialize<TestSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }

        // Install Playwright browsers on first run. The lifecycle runner verifies browsers once
        // and sets LIFECYCLE_SKIP_BROWSER_INSTALL=1 so parallel test processes never race installers.
        if (Environment.GetEnvironmentVariable("LIFECYCLE_SKIP_BROWSER_INSTALL") != "1")
        {
            var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (exitCode != 0)
                throw new InvalidOperationException($"Playwright browser install failed (exit code {exitCode}).");
        }

        // Launch browser
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = IsValidationMode,
            SlowMo = IsReportMode ? 500 : 0
        });

        // Create authenticated context using AuthHelper
        var authHelper = new AuthHelper(Settings);
        _context = await authHelper.GetAuthenticatedContextAsync(_browser);
    }

    public static async Task TearDownAsync()
    {
        if (_context != null) await _context.CloseAsync();
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}

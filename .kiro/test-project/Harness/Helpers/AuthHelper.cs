using System.Text.Json;
using Microsoft.Playwright;

namespace MigrationTests.Playwright;

/// <summary>
/// Handles browser context creation with stored auth state validation.
/// Validates cookie expiry before loading state to prevent silent auth failures.
/// </summary>
public class AuthHelper
{
    private readonly string _stateFilePath;
    private readonly TestSettings _settings;

    public AuthHelper(TestSettings settings)
    {
        _settings = settings;
        // Relative paths resolve against the test-project root (<T>\auth-state.json), not the bin.
        _stateFilePath = HarnessPaths.Resolve(settings.AuthStateFilePath);
    }

    /// <summary>
    /// Returns a BrowserContext with valid auth state loaded (if available and not expired).
    /// Falls back to a plain context if no valid state exists.
    /// </summary>
    public async Task<IBrowserContext> GetAuthenticatedContextAsync(IBrowser browser)
    {
        if (IsStoredStateValid())
        {
            return await browser.NewContextAsync(new BrowserNewContextOptions
            {
                StorageStatePath = _stateFilePath,
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                IgnoreHTTPSErrors = true
            });
        }

        // No valid state — return plain context (tests will detect auth redirect)
        return await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            IgnoreHTTPSErrors = true
        });
    }

    /// <summary>
    /// Checks if stored auth state file exists and session cookies are not expired.
    /// </summary>
    public bool IsStoredStateValid()
    {
        if (!File.Exists(_stateFilePath)) return false;

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("cookies", out var cookies)) return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var cookie in cookies.EnumerateArray())
            {
                if (!cookie.TryGetProperty("expires", out var expiresElement)) continue;
                var expires = expiresElement.GetDouble();
                if (expires <= 0) continue; // Session cookie — valid as long as it exists
                if (expires < now) return false; // Expired
            }

            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Determines if the given URL is an auth provider login page.
    /// Uses configurable patterns from TestSettings.AuthRedirectPatterns.
    /// </summary>
    public bool IsAuthRedirectPage(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        var patterns = _settings.AuthRedirectPatterns ?? new[] { "/login", "/oauth2/", "/signin" };
        return patterns.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}

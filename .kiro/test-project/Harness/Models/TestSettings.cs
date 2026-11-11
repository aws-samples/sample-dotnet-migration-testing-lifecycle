namespace MigrationTests.Playwright;

/// <summary>
/// Test infrastructure settings loaded from appsettings.test.json.
/// Each app fills in its own values.
/// </summary>
public class TestSettings
{
    /// <summary>URL of the target system being tested.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>URL of the reference system (for two-system comparison).</summary>
    public string ReferenceBaseUrl { get; set; } = "";

    /// <summary>Primary test entity ID (e.g., contract, order, customer ID).</summary>
    public int TestEntityId { get; set; }

    /// <summary>Timeout for page load operations (ms).</summary>
    public int PageLoadTimeoutMs { get; set; } = 30000;

    /// <summary>Timeout for dialog operations (ms).</summary>
    public int DialogTimeoutMs { get; set; } = 10000;

    /// <summary>Timeout for grid/data load operations (ms).</summary>
    public int GridLoadTimeoutMs { get; set; } = 10000;

    /// <summary>Path to saved authentication state file.</summary>
    public string AuthStateFilePath { get; set; } = "auth-state.json";

    /// <summary>Base directory for screenshots.</summary>
    public string ScreenshotBaseDir { get; set; } = "screenshots";

    /// <summary>URL patterns that indicate auth redirect (session expired).</summary>
    public string[]? AuthRedirectPatterns { get; set; }

    /// <summary>Auth provider settings.</summary>
    public AuthSettings Auth { get; set; } = new();

    /// <summary>Geometry diff tolerances and page content sanity checks (leaked template syntax, console errors).</summary>
    public VisualChecksSettings VisualChecks { get; set; } = new();

    /// <summary>
    /// Stack profile file name (without .json) under profiles/, e.g. "webforms-jqgrid__blazor-mudblazor".
    /// Empty = profiles/generic.json only.
    /// </summary>
    public string StackProfile { get; set; } = "";
}

/// <summary>
/// "VisualChecks" block of appsettings.test.json. Read by the harness (GlobalSetup.Settings) and by the
/// Reports tool (<see cref="Load"/>); every value defaults to the agreed behaviour when the block is absent.
/// </summary>
public class VisualChecksSettings
{
    public static readonly string[] DefaultLeakedTokens = { "<%@", "<%", "%>", "@code", "@page", "{{", "}}" };
    public static readonly string[] DefaultStandaloneTokens = { "undefined", "NaN", "[object Object]" };

    /// <summary>Substring tokens: any occurrence in the visible body text is leaked server/template syntax.</summary>
    public string[] LeakedTokens { get; set; } = DefaultLeakedTokens.ToArray();

    /// <summary>Whole-token matches only (not inside a word): "NaN" matches, "Nancy" does not.</summary>
    public string[] StandaloneTokens { get; set; } = DefaultStandaloneTokens.ToArray();

    /// <summary>Record browser console errors and unhandled page errors per step.</summary>
    public bool CaptureConsoleErrors { get; set; } = true;

    /// <summary>Reference vs Target tolerance for the layout.* geometry rows.</summary>
    public GeometryTolerance Geometry { get; set; } = new();

    /// <summary>Automatic element inventory (accessibility-based discovery and Reference/Target matching).</summary>
    public InventorySettings Inventory { get; set; } = new();

    /// <summary>Hover / focus / disabled style capture for matched interactive elements.</summary>
    public InteractiveStatesSettings InteractiveStates { get; set; } = new();

    /// <summary>Element inventory re-captured at extra viewport widths.</summary>
    public ResponsiveSettings Responsive { get; set; } = new();

    /// <summary>Properties never compared on non-interactive elements (no visual effect there), e.g. cursor.</summary>
    public string[] IgnoreProperties { get; set; } = { "cursor" };

    /// <summary>Extra page health checks (both sides; Reference hits are Reference defects).</summary>
    public PageHealthSettings PageHealth { get; set; } = new();

    /// <summary>Layout relationship checks between matched elements.</summary>
    public LayoutRelationSettings Layout { get; set; } = new();

    /// <summary>Severity recalibration of the style/state/mapped CSS rows (px thresholds, CIEDE2000 deltaE, WCAG contrast).</summary>
    public CssSeveritySettings Severity { get; set; } = new();

    /// <summary>Theme-level CSS groups: chat table size and example elements per group.</summary>
    public CssGroupsSettings Groups { get; set; } = new();

    /// <summary>Copied from the top-level "StackProfile" setting by <see cref="Load"/> (empty = generic only).</summary>
    public string StackProfile { get; set; } = "";

    /// <summary>Reads the VisualChecks block (plus the top-level StackProfile) from a settings file; a missing or unreadable file (or block) gives the defaults.</summary>
    public static VisualChecksSettings Load(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new();
            var s = System.Text.Json.JsonSerializer.Deserialize<TestSettings>(File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            var vc = s?.VisualChecks ?? new();
            if (string.IsNullOrWhiteSpace(vc.StackProfile)) vc.StackProfile = s?.StackProfile ?? "";
            return vc;
        }
        catch
        {
            return new();
        }
    }
}

public class InventorySettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Data rows inventoried per table/grid (repeated rows are grouped: only the first N are compared).</summary>
    public int MaxRowsPerGrid { get; set; } = 3;
    /// <summary>Cap on inventoried elements per capture (DOM order).</summary>
    public int MaxElements { get; set; } = 300;
    /// <summary>Minimum match score (0..1) for an automatic Reference/Target pair.</summary>
    public double MinMatchScore { get; set; } = 0.6;
}

public class InteractiveStatesSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>CSS states in which hover/focus are captured (they move the mouse and focus, so read-only states only).</summary>
    public string[] States { get; set; } = { "page_load" };
    /// <summary>Interactive, enabled elements hovered/focused per state (DOM order).</summary>
    public int MaxElements { get; set; } = 12;
}

public class ResponsiveSettings
{
    public bool Enabled { get; set; } = true;
    public string[] States { get; set; } = { "page_load" };
    /// <summary>Viewport widths (px); the page is re-inventoried at each width, then the original viewport is restored.</summary>
    public int[] Widths { get; set; } = { 1366, 768 };
}

public class PageHealthSettings
{
    /// <summary>Images that finished loading with naturalWidth 0.</summary>
    public bool BrokenImages { get; set; } = true;
    /// <summary>HTTP status &gt;= 400 or failed requests (same origin, or image/stylesheet/script/font).</summary>
    public bool FailedRequests { get; set; } = true;
    /// <summary>Visible headings/sections with no content up to the next heading.</summary>
    public bool EmptySections { get; set; } = true;
    /// <summary>Controls/buttons without an accessible name, images without alt.</summary>
    public bool Accessibility { get; set; } = true;
}

public class LayoutRelationSettings
{
    /// <summary>Spacing to the previous sibling: rows need |diff| &gt; this (and the Geometry tolerance).</summary>
    public double SpacingMinDiffPx { get; set; } = 4;
    /// <summary>Edges within this distance count as aligned.</summary>
    public double AlignTolerancePx { get; set; } = 2;
}

/// <summary>
/// A geometry row is flagged when |diff| &gt; MinDiffPx AND diff% &gt; MinDiffPct (diff% = |diff| / max(|Reference|, 1)).
/// A flagged row is HIGH when |diff| &gt; HighDiffPx OR diff% &gt; HighDiffPct, else MEDIUM.
/// </summary>
public class GeometryTolerance
{
    public double MinDiffPx { get; set; } = 4;
    public double MinDiffPct { get; set; } = 10;
    public double HighDiffPx { get; set; } = 40;
    public double HighDiffPct { get; set; } = 50;
}

/// <summary>
/// Severity rules for style/state/mapped CSS rows (CssSeverityRules). A difference at or below a px threshold is LOW
/// (not actionable), above it MEDIUM. Colors: CIEDE2000 deltaE &lt; DeltaELow LOW, up to DeltaEHigh MEDIUM, above it
/// HIGH on text/background of interactive or primary elements (else MEDIUM). A Target contrast below ContrastMin
/// where the Reference passes is HIGH. Unparsable values keep their severity.
/// </summary>
public class CssSeveritySettings
{
    public double FontSizeLowPx { get; set; } = 1;
    /// <summary>border-radius, padding*, margin*, border-*-width.</summary>
    public double BoxLowPx { get; set; } = 1;
    /// <summary>letter-spacing, line-height.</summary>
    public double TextMetricLowPx { get; set; } = 1;
    public double DeltaELow { get; set; } = 3;
    public double DeltaEHigh { get; set; } = 10;
    /// <summary>WCAG AA contrast ratio for normal text.</summary>
    public double ContrastMin { get; set; } = 4.5;
    /// <summary>font-weight pairs (either order) that are LOW.</summary>
    public string[][] LowFontWeightPairs { get; set; } = { new[] { "500", "600" } };
    /// <summary>Roles whose text/background color changes count as primary (HIGH above DeltaEHigh) even when not interactive.</summary>
    public string[] PrimaryRoles { get; set; } = { "heading", "columnheader", "banner", "navigation", "tab" };
}

public class CssGroupsSettings
{
    /// <summary>Actionable groups shown in the chat module-result and final views (the rest: reports/CSS_Groups.md).</summary>
    public int ChatTop { get; set; } = 15;
    /// <summary>Example element labels listed per group.</summary>
    public int ExampleElements { get; set; } = 3;
}

public class AuthSettings
{
    /// <summary>Login URL for the auth provider.</summary>
    public string LoginUrl { get; set; } = "";

    /// <summary>Username (if not interactive).</summary>
    public string Username { get; set; } = "";
}

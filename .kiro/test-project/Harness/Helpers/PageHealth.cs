using System.Text.RegularExpressions;
using Microsoft.Playwright;
using MigrationTests.Reporting;

namespace MigrationTests.Playwright;

/// <summary>
/// Content sanity checks (best effort, never fail a test):
/// - <see cref="ScanText"/>: leaked server/template syntax and standalone junk tokens in the visible body text;
/// - <see cref="Attach"/>/<see cref="Drain"/>: browser console errors and unhandled page errors, buffered per process
///   (one NUnit worker per process, same pattern as ModuleTestRunner.Current) and drained into each recorded step.
/// </summary>
public static class PageHealth
{
    public const int ExcerptMax = 120;
    public const int MessageMax = 200;

    private static readonly object Gate = new();
    private static readonly List<ContentIssue> Buffer = new();
    /// <summary>Failed-request URLs already buffered (cleared with the buffer).</summary>
    private static readonly HashSet<string> FailedUrls = new(StringComparer.Ordinal);

    /// <summary>Resource types whose failure is reported even when cross-origin (assets the page renders with).</summary>
    private static readonly HashSet<string> AssetTypes = new(StringComparer.OrdinalIgnoreCase) { "image", "stylesheet", "script", "font" };

    /// <summary>
    /// Subscribes the page's console and page-error events (CaptureConsoleErrors) and the failed-request events
    /// (PageHealth.FailedRequests: HTTP status &gt;= 400 or network failure, same-origin or asset types). Clears the buffer.
    /// </summary>
    public static void Attach(IPage page, VisualChecksSettings? settings = null)
    {
        settings ??= GlobalSetup.Settings.VisualChecks ?? new();
        lock (Gate) { Buffer.Clear(); FailedUrls.Clear(); }
        if (settings.CaptureConsoleErrors)
        {
            page.Console += (_, msg) =>
            {
                try
                {
                    if (msg.Type == "error") Add(ContentIssue.ConsoleError, msg.Text);
                }
                catch { /* best effort */ }
            };
            page.PageError += (_, error) => Add(ContentIssue.PageError, error);
        }
        if (settings.PageHealth?.FailedRequests ?? true)
        {
            page.Response += (_, response) =>
            {
                try
                {
                    if (response.Status < 400) return;
                    var req = response.Request;
                    if (ReportRequest(page.Url, req.Url, req.ResourceType))
                        AddFailedRequest($"{response.Status} {req.Method} {req.Url}", req.Url);
                }
                catch { /* best effort */ }
            };
            page.RequestFailed += (_, req) =>
            {
                try
                {
                    if (!IsAborted(req.Failure) && ReportRequest(page.Url, req.Url, req.ResourceType))
                        AddFailedRequest($"failed ({req.Failure}) {req.Method} {req.Url}", req.Url);
                }
                catch { /* best effort */ }
            };
        }
    }

    /// <summary>
    /// Requests the browser cancelled itself (postback/navigation, a superseded load, a closed Blazor circuit probe):
    /// Chromium net::ERR_ABORTED, Firefox NS_BINDING_ABORTED, WebKit "cancelled". Not a defect the app can fix.
    /// </summary>
    internal static bool IsAborted(string? failure) =>
        !string.IsNullOrEmpty(failure)
        && (failure.Contains("ERR_ABORTED", StringComparison.OrdinalIgnoreCase)
            || failure.Contains("NS_BINDING_ABORTED", StringComparison.OrdinalIgnoreCase)
            || failure.Trim().Equals("cancelled", StringComparison.OrdinalIgnoreCase));

    /// <summary>Same origin as the page, or an asset type (image/stylesheet/script/font) from anywhere.</summary>
    internal static bool ReportRequest(string? pageUrl, string? requestUrl, string? resourceType)
    {
        if (!string.IsNullOrEmpty(resourceType) && AssetTypes.Contains(resourceType)) return true;
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var p) || !Uri.TryCreate(requestUrl, UriKind.Absolute, out var r)) return false;
        return string.Equals(p.Scheme, r.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Host, r.Host, StringComparison.OrdinalIgnoreCase) && p.Port == r.Port;
    }

    /// <summary>One failed-request issue per URL (the first status seen) until the buffer is drained.</summary>
    internal static void AddFailedRequest(string text, string url)
    {
        lock (Gate)
            if (!FailedUrls.Add(url)) return;
        Add(ContentIssue.FailedRequest, text);
    }

    private static string Trim(string message)
    {
        var text = (message ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > MessageMax ? text[..MessageMax] + "..." : text;
    }

    /// <summary>Adds a console/page error to the buffer (message trimmed to one line of at most 200 chars).</summary>
    public static void Add(string kind, string? message)
    {
        var text = Trim(message ?? "");
        lock (Gate) Buffer.Add(new ContentIssue { Kind = kind, Excerpt = text });
    }

    /// <summary>Returns and clears the buffered console/page errors.</summary>
    public static List<ContentIssue> Drain()
    {
        lock (Gate)
        {
            var list = Buffer.ToList();
            Buffer.Clear();
            FailedUrls.Clear();
            return list;
        }
    }

    /// <summary>
    /// One leaked-syntax issue per distinct offending text line, listing every matched token in settings order.
    /// LeakedTokens match as substrings; StandaloneTokens only as whole tokens (not inside a word or [...]).
    /// The excerpt is the trimmed line, cut to 120 chars around the first match.
    /// </summary>
    public static List<ContentIssue> ScanText(string? innerText, VisualChecksSettings? settings = null)
    {
        settings ??= new VisualChecksSettings();
        var issues = new List<ContentIssue>();
        if (string.IsNullOrEmpty(innerText)) return issues;
        var leaked = (settings.LeakedTokens ?? Array.Empty<string>()).Where(t => !string.IsNullOrEmpty(t)).ToList();
        var standalone = (settings.StandaloneTokens ?? Array.Empty<string>()).Where(t => !string.IsNullOrEmpty(t))
            .Select(t => (Token: t, Rx: new Regex(@"(?<![\w\[])" + Regex.Escape(t) + @"(?![\w\]])", RegexOptions.CultureInvariant)))
            .ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in innerText.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var tokens = new List<string>();
            var first = int.MaxValue;
            foreach (var t in leaked)
            {
                var i = line.IndexOf(t, StringComparison.Ordinal);
                if (i < 0) continue;
                tokens.Add(t);
                first = Math.Min(first, i);
            }
            foreach (var (t, rx) in standalone)
            {
                var m = rx.Match(line);
                if (!m.Success) continue;
                tokens.Add(t);
                first = Math.Min(first, m.Index);
            }
            if (tokens.Count == 0) continue;
            var excerpt = Excerpt(line, first);
            if (seen.Add(excerpt))
                issues.Add(new ContentIssue { Kind = ContentIssue.LeakedSyntax, Tokens = tokens, Excerpt = excerpt });
        }
        return issues;
    }

    private static string Excerpt(string line, int matchIndex)
    {
        if (line.Length <= ExcerptMax) return line;
        var start = Math.Max(0, Math.Min(matchIndex - 20, line.Length - ExcerptMax));
        var cut = line.Substring(start, ExcerptMax);
        return (start > 0 ? "..." : "") + cut + (start + ExcerptMax < line.Length ? "..." : "");
    }
}

using Microsoft.Playwright;
using System.Text.Json;

namespace MigrationTests.Playwright;

/// <summary>
/// Auto-discovers ALL visible DOM elements and captures their computed CSS styles.
/// Used for cross-system visual comparison (Reference vs Target).
/// </summary>
public static class CssCompareHelper
{
    public record CssDiff(string ControlName, string Property, string ReferenceValue, string TargetValue);

    /// <summary>
    /// Discovers all visible elements on the page and captures their computed styles.
    /// Saves to: visual-compare/css_full_{system}_{stateName}_full.json
    /// </summary>
    public static async Task DiscoverAndCaptureAll(IPage page, string system, string stateName)
    {
        // Lifecycle mode (design §6): LIFECYCLE_PAGE_DIR set AND a current runner.
        var pageDir = Environment.GetEnvironmentVariable("LIFECYCLE_PAGE_DIR");
        var runner = ModuleTestRunner.Current;
        if (!string.IsNullOrWhiteSpace(pageDir) && runner != null)
        {
            await CaptureLifecycle(page, pageDir, runner, stateName);
            return;
        }

        var outputDir = Path.Combine(AppContext.BaseDirectory, "visual-compare");
        Directory.CreateDirectory(outputDir);

        var styles = await CaptureFullDom(page);
        if (styles != null)
        {
            var json = JsonSerializer.Serialize(styles, new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(outputDir, $"css_full_{system}_{stateName}_full.json");
            await File.WriteAllTextAsync(filePath, json);
        }
    }

    /// <summary>
    /// Lifecycle-mode capture: writes only to &lt;pageDir&gt;/runs/Module&lt;X&gt;/&lt;System&gt;/css/
    /// (css_full_&lt;state&gt;.json plus css_mapped_&lt;state&gt;.json from state-map/css-map.json).
    /// </summary>
    private static async Task CaptureLifecycle(IPage page, string pageDir, ModuleTestRunner runner, string stateName)
    {
        var system = runner.SystemName; // "Reference" | "Target" (the lower-case argument is ignored)
        var outputDir = Path.Combine(pageDir, "runs", $"Module{runner.ModuleName}", system, "css");
        Directory.CreateDirectory(outputDir);
        var indented = new JsonSerializerOptions { WriteIndented = true };

        var styles = await CaptureFullDom(page);
        if (styles != null)
            await File.WriteAllTextAsync(Path.Combine(outputDir, $"css_full_{stateName}.json"),
                JsonSerializer.Serialize(styles, indented));

        var mapPath = Path.Combine(pageDir, "state-map", "css-map.json");
        MigrationTests.Reporting.CssMapFile? map = null;
        if (File.Exists(mapPath))
        {
            try
            {
                map = JsonSerializer.Deserialize<MigrationTests.Reporting.CssMapFile>(
                    await File.ReadAllTextAsync(mapPath), MigrationTests.Reporting.ReportJson.Options);
            }
            catch (Exception e)
            {
                runner.Log($"css-map.json unreadable ({e.Message}); mapped CSS skipped");
            }
        }

        if (map == null)
        {
            runner.CssMapMissing = true;
        }
        else
        {
            var mapped = new Dictionary<string, Dictionary<string, string>>();
            var isReference = string.Equals(system, "Reference", StringComparison.OrdinalIgnoreCase);
            foreach (var el in map.Elements.Where(e => string.Equals(e.State, stateName, StringComparison.Ordinal)))
            {
                var selector = isReference ? el.Reference : el.Target;
                var props = (el.Properties?.Keys ?? Enumerable.Empty<string>())
                    .Concat(el.Accepted?.Keys ?? Enumerable.Empty<string>()).Distinct().ToArray();
                mapped[el.Name] = await CaptureElement(page, selector, props);
            }
            await File.WriteAllTextAsync(Path.Combine(outputDir, $"css_mapped_{stateName}.json"),
                JsonSerializer.Serialize(mapped, indented));
        }

        // Automatic inventory (element discovery, states, responsive, page health); best effort, logs its time.
        await ElementInventory.CaptureAsync(page, runner, map, stateName, outputDir, GlobalSetup.Settings.VisualChecks ?? new());

        if (!runner.CssStatesCaptured.Contains(stateName)) runner.CssStatesCaptured.Add(stateName);
    }

    private static readonly Dictionary<string, string> MissingElement = new() { ["__missing"] = "true" };

    /// <summary>Prefix of the normalization keys (style.effectiveBackground, style.renderedFont, style.interactive).</summary>
    public const string StylePrefix = "style.";

    /// <summary>
    /// In-page helpers shared by the mapped capture and the element inventory (pasted at the top of the evaluated function):
    /// __effBg(el) = first non-transparent background-color of el or an ancestor (default white, the canvas color);
    /// __renderedFont(fontFamily) = first family of the stack that renders: generic families always do, a named family
    /// must pass document.fonts.check (declared web fonts must be loaded) and change the width of a probe text
    /// against both monospace and serif fallbacks (installed system font);
    /// __interactive(el) = native control, link with href, interactive ARIA role or explicit tabindex.
    /// </summary>
    internal const string JsHelpers = @"
                const __isClear = c => !c || c === 'transparent' || /^rgba\(.*,\s*0(\.0+)?\)$/.test(c);
                const __effBg = el => {
                    for (let n = el; n && n.nodeType === 1; n = n.parentElement) {
                        const c = getComputedStyle(n).backgroundColor;
                        if (!__isClear(c)) return c;
                    }
                    return 'rgb(255, 255, 255)';
                };
                const __generic = ['serif','sans-serif','monospace','cursive','fantasy','system-ui','ui-serif','ui-sans-serif','ui-monospace','ui-rounded','emoji','math','fangsong'];
                window.__mtFontCache = window.__mtFontCache || {};
                const __fontAvailable = f => {
                    try { if (document.fonts && !document.fonts.check('12px ""' + f + '""')) return false; } catch (e) { }
                    try {
                        const cv = window.__mtCanvas || (window.__mtCanvas = document.createElement('canvas'));
                        const ctx = cv.getContext('2d');
                        const probe = 'mmmmmmmmmmlli1WQ@#';
                        for (const base of ['monospace', 'serif']) {
                            ctx.font = '72px ' + base; const w0 = ctx.measureText(probe).width;
                            ctx.font = '72px ""' + f + '"", ' + base; const w1 = ctx.measureText(probe).width;
                            if (Math.abs(w1 - w0) > 0.01) return true;
                        }
                    } catch (e) { return true; }
                    return false;
                };
                const __renderedFont = ff => {
                    ff = ff || '';
                    if (window.__mtFontCache[ff] !== undefined) return window.__mtFontCache[ff];
                    const fams = ff.split(',').map(s => s.trim().replace(/^['""]|['""]$/g, '')).filter(s => s);
                    let r = fams.length ? fams[fams.length - 1] : '';
                    for (const f of fams) {
                        if (__generic.includes(f.toLowerCase())) { r = f.toLowerCase(); break; }
                        if (__fontAvailable(f)) { r = f; break; }
                    }
                    window.__mtFontCache[ff] = r;
                    return r;
                };
                const __interactive = el => {
                    const t = el.tagName;
                    if (/^(BUTTON|INPUT|SELECT|TEXTAREA|SUMMARY)$/.test(t)) return !(t === 'INPUT' && el.type === 'hidden');
                    if (t === 'A' && el.hasAttribute('href')) return true;
                    const role = (el.getAttribute('role') || '').toLowerCase();
                    if (/^(button|link|checkbox|radio|tab|menuitem|menuitemcheckbox|menuitemradio|switch|combobox|textbox|searchbox|option|slider|spinbutton)$/.test(role)) return true;
                    return el.hasAttribute('tabindex') && el.tabIndex >= 0;
                };";

    /// <summary>Prefix of the geometry keys written next to the computed-style properties (never passed to getComputedStyle).</summary>
    public const string LayoutPrefix = "layout.";

    /// <summary>
    /// getComputedStyle for the listed properties on the first match of the selector, plus the geometry keys
    /// (page coordinates, px, rounded to 0.1): layout.x/y/width/height = bounding box of the first visible match
    /// (first match when none is visible), layout.visibleCount = visible matches, layout.lastBottom = bottom of
    /// the lowest visible match (only when one is visible), layout.contentTop = top of the first painted content
    /// (non-blank text, form control, image) inside the first visible match. Visible = height &gt; 0 and display is not none.
    /// </summary>
    private static async Task<Dictionary<string, string>> CaptureElement(IPage page, string selector, string[] props)
    {
        if (string.IsNullOrWhiteSpace(selector)) return new(MissingElement);
        try
        {
            var all = page.Locator(selector);
            try { await all.First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 3000 }); } catch { }
            if (await all.CountAsync() == 0) return new(MissingElement);
            var cssProps = props.Where(p => !p.StartsWith(LayoutPrefix, StringComparison.Ordinal)).ToArray();
            var raw = await all.EvaluateAllAsync<string>(@"(els, props) => {
                " + JsHelpers + @"
                const out = {};
                if (els.length === 0) return JSON.stringify({ __missing: 'true' });
                const cs = getComputedStyle(els[0]);
                props.forEach(p => { out[p] = cs.getPropertyValue(p); });
                // Normalization inputs (style.* keys, never compared raw): background seen through transparent
                // ancestors, first font family that actually renders, interactive element (ignore-list scope).
                out['style.effectiveBackground'] = __effBg(els[0]);
                out['style.renderedFont'] = __renderedFont(cs.fontFamily);
                out['style.interactive'] = String(__interactive(els[0]));
                const r1 = v => String(Math.round(v * 10) / 10);
                const sx = window.scrollX || 0, sy = window.scrollY || 0;
                let first = null, visible = 0, lastBottom = null;
                els.forEach(el => {
                    const r = el.getBoundingClientRect();
                    if (r.height > 0 && getComputedStyle(el).display !== 'none') {
                        visible++;
                        if (first === null) first = r;
                        const b = r.bottom + sy;
                        if (lastBottom === null || b > lastBottom) lastBottom = b;
                    }
                });
                const firstEl = els.find(el => { const r = el.getBoundingClientRect(); return r.height > 0 && getComputedStyle(el).display !== 'none'; });
                if (first === null) first = els[0].getBoundingClientRect();
                // layout.contentTop = top of the first painted content (non-blank text, form control, image) inside the
                // first visible match: a container whose widget sits below empty space inside it still shows the gap.
                if (firstEl) {
                    let top = null;
                    const take = r => { if (r && r.height > 0 && r.width > 0 && (top === null || r.top < top)) top = r.top; };
                    const w = document.createTreeWalker(firstEl, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
                    for (let n = w.currentNode; n; n = w.nextNode()) {
                        if (n.nodeType === 3) {
                            if (!n.textContent.trim()) continue;
                            const rg = document.createRange(); rg.selectNodeContents(n); take(rg.getBoundingClientRect());
                        } else if (/^(INPUT|SELECT|BUTTON|TEXTAREA|IMG|SVG|svg)$/.test(n.tagName)) {
                            if (getComputedStyle(n).visibility !== 'hidden') take(n.getBoundingClientRect());
                        }
                    }
                    if (top !== null) out['layout.contentTop'] = r1(top + sy);
                }
                out['layout.x'] = r1(first.left + sx);
                out['layout.y'] = r1(first.top + sy);
                out['layout.width'] = r1(first.width);
                out['layout.height'] = r1(first.height);
                out['layout.visibleCount'] = String(visible);
                if (lastBottom !== null) out['layout.lastBottom'] = r1(lastBottom);
                return JSON.stringify(out);
            }", cssProps);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(raw ?? "{}") ?? new(MissingElement);
        }
        catch
        {
            return new(MissingElement);
        }
    }

    private static async Task<Dictionary<string, Dictionary<string, string>>?> CaptureFullDom(IPage page)
    {
        // NOTE: JS returns a JSON *string* (not a nested object). Playwright's
        // EvaluateAsync<Dictionary<string,Dictionary<string,string>>> fails to deserialize a
        // nested-object return and silently yields 0 keys, writing an empty "{}" capture.
        // Returning a JSON string and deserializing in C# captures every element reliably.
        var rawJson = await page.EvaluateAsync<string>(@"() => {
            const results = {};
            const props = ['font-family','font-size','font-weight','color','background-color',
                          'border','padding','margin','text-align','display','width','height',
                          'line-height','border-radius','box-shadow'];
            const elements = document.querySelectorAll('h1,h2,h3,h4,th,td,tr,input,select,button,label,span,div,a,img,textarea');
            let idx = 0;
            elements.forEach(el => {
                if (el.offsetParent === null && el.style.display !== 'contents') return; // hidden
                idx++;
                const tag = el.tagName.toLowerCase();
                const cls = el.className ? '.' + el.className.toString().split(' ').filter(c=>c).slice(0,2).join('.') : '';
                const id = el.id ? '#' + el.id : '';
                const key = `${tag}${id}${cls}[${idx}]`;
                const computed = getComputedStyle(el);
                const styleMap = {};
                props.forEach(p => { styleMap[p] = computed.getPropertyValue(p); });
                results[key] = styleMap;
            });
            return JSON.stringify(results);
        }");

        Dictionary<string, Dictionary<string, string>>? styles = null;
        if (!string.IsNullOrEmpty(rawJson))
        {
            try { styles = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(rawJson); }
            catch { styles = null; }
        }
        return styles;
    }

    /// <summary>
    /// Compares Reference vs Target CSS captures for a given state.
    /// Returns list of differences.
    /// </summary>
    public static async Task<List<CssDiff>> CompareStyles(string stateName)
    {
        var diffs = new List<CssDiff>();
        var dir = Path.Combine(AppContext.BaseDirectory, "visual-compare");

        var refFile = Path.Combine(dir, $"css_full_reference_{stateName}_full.json");
        var targetFile = Path.Combine(dir, $"css_full_target_{stateName}_full.json");

        if (!File.Exists(refFile) || !File.Exists(targetFile))
            return diffs;

        var refData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            await File.ReadAllTextAsync(refFile)) ?? new();
        var targetData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            await File.ReadAllTextAsync(targetFile)) ?? new();

        foreach (var (key, refStyles) in refData)
        {
            if (!targetData.TryGetValue(key, out var targetStyles)) continue;

            foreach (var (prop, refVal) in refStyles)
            {
                if (!targetStyles.TryGetValue(prop, out var targetVal)) continue;
                if (refVal == targetVal) continue;

                // Skip noise
                if (IsNoiseDiff(prop, refVal, targetVal)) continue;

                diffs.Add(new CssDiff(key, prop, refVal, targetVal));
            }
        }

        return diffs;
    }

    /// <summary>
    /// Compares full captures and generates a diff report markdown file.
    /// </summary>
    public static async Task<string> CompareFullCaptures(string stateName)
    {
        var diffs = await CompareStyles(stateName);
        if (diffs.Count == 0) return "No CSS differences found.";

        var lines = new List<string>
        {
            $"# CSS Diff Report — {stateName}",
            "",
            $"Total differences: {diffs.Count}",
            "",
            "| Element | Property | Reference | Target |",
            "|---------|----------|-----------|--------|"
        };

        foreach (var d in diffs.Take(50))
            lines.Add($"| {Truncate(d.ControlName, 40)} | {d.Property} | `{Truncate(d.ReferenceValue, 25)}` | `{Truncate(d.TargetValue, 25)}` |");

        if (diffs.Count > 50)
            lines.Add($"| ... | ... | {diffs.Count - 50} more | ... |");

        var outputPath = Path.Combine(AppContext.BaseDirectory, "visual-compare", $"css_full_diff_{stateName}.md");
        var content = string.Join("\n", lines);
        await File.WriteAllTextAsync(outputPath, content);
        return content;
    }

    /// <summary>
    /// Format-only difference: ±1px on width/height/margin/padding, else the values are equal after
    /// <see cref="MigrationTests.Reporting.StyleNormalizer"/> (colors as rgba, 0.5px rounding, normal = 400, start = left, no border = none).
    /// </summary>
    internal static bool IsNoiseDiff(string property, string refVal, string targetVal)
    {
        // ±1px on dimensions
        if ((property == "width" || property == "height" || property == "margin" || property == "padding")
            && TryParsePx(refVal, out var refPx) && TryParsePx(targetVal, out var tgtPx)
            && Math.Abs(refPx - tgtPx) <= 1)
            return true;

        return MigrationTests.Reporting.StyleNormalizer.Equivalent(property, refVal, targetVal);
    }

    private static bool TryParsePx(string value, out double px)
    {
        px = 0;
        if (string.IsNullOrEmpty(value)) return false;
        var cleaned = value.Replace("px", "").Trim();
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out px);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";
}

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using MigrationTests.Reporting;

namespace MigrationTests.Playwright;

/// <summary>
/// Automatic element inventory at one CSS state (lifecycle mode, called by CssCompareHelper after the mapped capture):
/// every visible interactive/content element with role, accessible name (in-page accname approximation), box, DOM path,
/// container/order, normalized styles and layout facts; plus hover/focus/disabled styles (InteractiveStates), the same
/// inventory at other viewport widths (Responsive) and page health facts (broken images, empty sections, a11y gaps).
/// Writes runs/Module&lt;X&gt;/&lt;System&gt;/css/inventory_&lt;state&gt;.json. Best effort: never fails the test.
/// </summary>
public static class ElementInventory
{
    public const string MapAttribute = "data-mt-map";

    public static async Task CaptureAsync(IPage page, ModuleTestRunner runner, CssMapFile? map, string state,
        string outputDir, VisualChecksSettings vc)
    {
        if (!(vc.Inventory?.Enabled ?? true)) return;
        var sw = Stopwatch.StartNew();
        var inv = vc.Inventory ?? new InventorySettings();
        var isReference = string.Equals(runner.SystemName, "Reference", StringComparison.OrdinalIgnoreCase);
        try
        {
            await TagMapElements(page, map, state, isReference);
            var opts = new Dictionary<string, object>
            {
                ["maxRows"] = Math.Max(1, inv.MaxRowsPerGrid),
                ["maxElements"] = Math.Max(1, inv.MaxElements),
                ["health"] = true,
                ["brokenImages"] = vc.PageHealth?.BrokenImages ?? true,
                ["emptySections"] = vc.PageHealth?.EmptySections ?? true,
                ["a11y"] = vc.PageHealth?.Accessibility ?? true
            };
            var root = await Evaluate(page, opts);
            await UntagMapElements(page);
            if (root == null) { runner.Log($"[inventory] {state} skipped: no result"); return; }
            root["state"] = state;

            var elements = root["elements"] as JsonArray ?? new JsonArray();
            if ((vc.InteractiveStates?.Enabled ?? true) && Contains(vc.InteractiveStates?.States, state))
                await CaptureInteractiveStates(page, elements, Math.Max(0, vc.InteractiveStates?.MaxElements ?? 12), runner.Log);

            if ((vc.Responsive?.Enabled ?? true) && Contains(vc.Responsive?.States, state))
                root["responsive"] = await CaptureResponsive(page, opts, vc.Responsive?.Widths ?? Array.Empty<int>(), root, runner);

            var issues = HealthIssues(root["health"]);
            if (issues.Count > 0) runner.AddContentIssues(runner.CurrentStep, issues);

            root["captureMs"] = sw.ElapsedMilliseconds;
            Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(Path.Combine(outputDir, $"inventory_{state}.json"),
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            runner.Log($"[inventory] {state} {elements.Count} elements, {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            try { await UntagMapElements(page); } catch { }
            runner.Log($"[inventory] {state} skipped: {FirstLine(e.Message)}");
        }
    }

    private static bool Contains(string[]? states, string state) =>
        states == null || states.Length == 0 || states.Contains(state, StringComparer.Ordinal);

    private static string FirstLine(string s) => (s ?? "").Split('\n')[0].Trim();

    /// <summary>Marks the first visible match of each css-map element of the state so the inventory records its name (explicit pairs).</summary>
    private static async Task TagMapElements(IPage page, CssMapFile? map, string state, bool isReference)
    {
        if (map == null) return;
        foreach (var el in map.Elements.Where(e => string.Equals(e.State, state, StringComparison.Ordinal)))
        {
            var selector = isReference ? el.Reference : el.Target;
            if (string.IsNullOrWhiteSpace(selector)) continue;
            try
            {
                var loc = page.Locator(selector).Filter(new() { Visible = true }).First;
                if (await loc.CountAsync() == 0) continue;
                await loc.EvaluateAsync("(e, n) => { if (!e.hasAttribute('" + MapAttribute + "')) e.setAttribute('" + MapAttribute + "', n); }",
                    el.Name, new() { Timeout = 1000 });
            }
            catch { /* selector problems are reported by the mapped capture */ }
        }
    }

    private static Task UntagMapElements(IPage page) =>
        page.EvaluateAsync("() => document.querySelectorAll('[" + MapAttribute + "]').forEach(e => e.removeAttribute('" + MapAttribute + "'))");

    private static async Task<JsonObject?> Evaluate(IPage page, Dictionary<string, object> opts)
    {
        var raw = await page.EvaluateAsync<string>(InventoryScript, opts);
        return string.IsNullOrEmpty(raw) ? null : JsonNode.Parse(raw) as JsonObject;
    }

    /// <summary>
    /// hover.* and focus.* styles of the first N interactive, enabled elements (transitions off while capturing).
    /// Chromium: the :hover / :focus / :focus-visible rules are forced through CDP CSS.forcePseudoState, so no mouse,
    /// focus or blur events reach the app (no blur validation, no popovers opened on focus, no Blazor
    /// @onfocus/@onblur round trips) and the module's later functional steps run on an untouched page.
    /// Other browsers (no CDP): real hover, and focus only on controls that do not take text entry or open a list
    /// (text inputs, textareas, selects, comboboxes are skipped), followed by mouse reset and blur.
    /// </summary>
    internal static async Task CaptureInteractiveStates(IPage page, JsonArray elements, int max, Action<string> log)
    {
        var targets = elements.OfType<JsonObject>()
            .Where(e => e["interactive"]?.GetValue<bool>() == true && e["disabled"]?.GetValue<bool>() != true)
            .Take(max).ToList();
        if (targets.Count == 0) return;
        await page.EvaluateAsync(@"() => { window.__mtScroll = [window.scrollX, window.scrollY];
            const s = document.createElement('style'); s.id = '__mt_notrans';
            s.textContent = '*,*::before,*::after{transition:none !important;animation-duration:0s !important;animation-delay:0s !important;}';
            document.head.appendChild(s); }");
        ICDPSession? cdp = null;
        try
        {
            cdp = await OpenPseudoStateSession(page);
            if (cdp != null)
            {
                var doc = await cdp.SendAsync("DOM.getDocument", new() { ["depth"] = 0 });
                var rootId = doc!.Value.GetProperty("root").GetProperty("nodeId").GetInt32();
                foreach (var e in targets)
                {
                    var path = e["domPath"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(path)) continue;
                    var states = StatesOf(e);
                    try
                    {
                        var q = await cdp.SendAsync("DOM.querySelector", new() { ["nodeId"] = rootId, ["selector"] = path });
                        var nodeId = q?.GetProperty("nodeId").GetInt32() ?? 0;
                        if (nodeId == 0) { log($"[inventory] states {path} skipped: not found"); continue; }
                        var loc = page.Locator("css=" + path).First;
                        await ForcePseudo(cdp, nodeId, "hover");
                        Merge(states, "hover.", await loc.EvaluateAsync<string>(StateScript, null, new() { Timeout = 1000 }));
                        await ForcePseudo(cdp, nodeId, "focus", "focus-visible");
                        Merge(states, "focus.", await loc.EvaluateAsync<string>(StateScript, null, new() { Timeout = 1000 }));
                        await ForcePseudo(cdp, nodeId);
                    }
                    catch (Exception ex) { log($"[inventory] states {path} skipped: {FirstLine(ex.Message)}"); }
                }
                return;
            }

            // Fallback without CDP: real events, never focusing text-entry or list controls.
            foreach (var e in targets)
            {
                var path = e["domPath"]?.GetValue<string>();
                if (string.IsNullOrEmpty(path)) continue;
                var states = StatesOf(e);
                var loc = page.Locator("css=" + path).First;
                try
                {
                    await loc.HoverAsync(new() { Force = true, Timeout = 1000 });
                    Merge(states, "hover.", await loc.EvaluateAsync<string>(StateScript, null, new() { Timeout = 1000 }));
                }
                catch (Exception ex) { log($"[inventory] hover {path} skipped: {FirstLine(ex.Message)}"); }
                try { await page.Mouse.MoveAsync(0, 0); } catch { }
                if (!FocusSafeWithoutCdp(e)) continue;
                try
                {
                    await loc.FocusAsync(new() { Timeout = 1000 });
                    Merge(states, "focus.", await loc.EvaluateAsync<string>(StateScript, null, new() { Timeout = 1000 }));
                }
                catch (Exception ex) { log($"[inventory] focus {path} skipped: {FirstLine(ex.Message)}"); }
                try { await page.EvaluateAsync("() => { if (document.activeElement && document.activeElement.blur) document.activeElement.blur(); }"); } catch { }
            }
        }
        finally
        {
            if (cdp != null) { try { await cdp.DetachAsync(); } catch { } }
            else { try { await page.Mouse.MoveAsync(0, 0); } catch { } }
            await page.EvaluateAsync(@"() => { const s = document.getElementById('__mt_notrans'); if (s) s.remove();
                if (window.__mtScroll) window.scrollTo(window.__mtScroll[0], window.__mtScroll[1]); }");
        }
    }

    private static JsonObject StatesOf(JsonObject e)
    {
        var states = e["states"] as JsonObject ?? new JsonObject();
        e["states"] = states;
        return states;
    }

    /// <summary>CDP session with DOM and CSS enabled, or null when the browser has no CDP (Firefox/WebKit).</summary>
    private static async Task<ICDPSession?> OpenPseudoStateSession(IPage page)
    {
        ICDPSession? cdp = null;
        try
        {
            cdp = await page.Context.NewCDPSessionAsync(page);
            await cdp.SendAsync("DOM.enable");
            await cdp.SendAsync("CSS.enable");
            return cdp;
        }
        catch
        {
            if (cdp != null) { try { await cdp.DetachAsync(); } catch { } }
            return null;
        }
    }

    private static Task ForcePseudo(ICDPSession cdp, int nodeId, params string[] classes) =>
        cdp.SendAsync("CSS.forcePseudoState", new() { ["nodeId"] = nodeId, ["forcedPseudoClasses"] = classes });

    /// <summary>Without CDP, focus is only applied to controls whose focus does not start text entry or open a list.</summary>
    internal static bool FocusSafeWithoutCdp(JsonObject e)
    {
        var tag = e["tag"]?.GetValue<string>() ?? "";
        var role = e["role"]?.GetValue<string>() ?? "";
        var type = e["inputType"]?.GetValue<string>() ?? "";
        if (tag is "textarea" or "select") return false;
        if (role is "textbox" or "searchbox" or "combobox" or "listbox" or "spinbutton" or "slider") return false;
        if (tag == "input" && type is not ("button" or "submit" or "reset" or "image" or "checkbox" or "radio")) return false;
        return true;
    }

    private static void Merge(JsonObject states, string prefix, string? json)
    {
        if (string.IsNullOrEmpty(json) || JsonNode.Parse(json) is not JsonObject o) return;
        foreach (var (k, v) in o) states[prefix + k] = v?.GetValue<string>() ?? "";
    }

    /// <summary>Inventory (no health, no states) at each width; the original viewport is restored afterwards.</summary>
    private static async Task<JsonObject> CaptureResponsive(IPage page, Dictionary<string, object> opts, int[] widths,
        JsonObject main, ModuleTestRunner runner)
    {
        var result = new JsonObject();
        var original = page.ViewportSize;
        if (original == null || widths.Length == 0) return result;
        var noHealth = new Dictionary<string, object>(opts) { ["health"] = false };
        try
        {
            foreach (var w in widths.Where(w => w > 0).Distinct())
            {
                if (w == original.Width)
                {
                    result[w.ToString()] = new JsonObject { ["viewport"] = main["viewport"]?.DeepClone(), ["elements"] = main["elements"]?.DeepClone() };
                    continue;
                }
                await page.SetViewportSizeAsync(w, original.Height);
                await page.WaitForTimeoutAsync(300);
                var snap = await Evaluate(page, noHealth);
                if (snap != null)
                    result[w.ToString()] = new JsonObject { ["viewport"] = snap["viewport"]?.DeepClone(), ["elements"] = snap["elements"]?.DeepClone() };
            }
        }
        catch (Exception e) { runner.Log($"[inventory] responsive skipped: {FirstLine(e.Message)}"); }
        finally
        {
            if (page.ViewportSize?.Width != original.Width || page.ViewportSize?.Height != original.Height)
            {
                await page.SetViewportSizeAsync(original.Width, original.Height);
                await page.WaitForTimeoutAsync(300);
            }
        }
        return result;
    }

    /// <summary>Health facts of the inventory → content issues (broken-image, empty-section, a11y), excerpts ≤ 200 chars.</summary>
    internal static List<ContentIssue> HealthIssues(JsonNode? health)
    {
        var list = new List<ContentIssue>();
        if (health is not JsonObject h) return list;
        void Add(string key, string kind, Func<string, string> text)
        {
            if (h[key] is not JsonArray arr) return;
            foreach (var v in arr.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).Distinct())
            {
                var t = text(v).Replace("\r", " ").Replace("\n", " ").Trim();
                if (t.Length > PageHealth.MessageMax) t = t[..PageHealth.MessageMax] + "...";
                list.Add(new ContentIssue { Kind = kind, Excerpt = t });
            }
        }
        Add("brokenImages", ContentIssue.BrokenImage, v => "image did not load: " + v);
        Add("emptySections", ContentIssue.EmptySection, v => v);
        Add("a11y", ContentIssue.Accessibility, v => v);
        return list;
    }

    /// <summary>Computed styles of one element for hover./focus. rows (JSON string).</summary>
    internal const string StateScript = @"(el) => {
        " + CssCompareHelper.JsHelpers + @"
        const cs = getComputedStyle(el);
        const side = s => cs.getPropertyValue('border-' + s + '-width') + ' ' + cs.getPropertyValue('border-' + s + '-style') + ' ' + cs.getPropertyValue('border-' + s + '-color');
        return JSON.stringify({
            'color': cs.color,
            'background-color': __effBg(el),
            'border-top': side('top'), 'border-right': side('right'), 'border-bottom': side('bottom'), 'border-left': side('left'),
            'outline': cs.outlineWidth + ' ' + cs.outlineStyle + ' ' + cs.outlineColor,
            'box-shadow': cs.boxShadow,
            'text-decoration': cs.textDecorationLine
        });
    }";

    /// <summary>
    /// The inventory script. Returns a JSON string { viewport, elements, health } (nested objects returned from
    /// EvaluateAsync do not deserialize reliably, see CssCompareHelper.CaptureFullDom).
    /// </summary>
    internal const string InventoryScript = @"(opts) => {
        " + CssCompareHelper.JsHelpers + @"
        const sx = window.scrollX || 0, sy = window.scrollY || 0;
        const r1 = v => Math.round(v * 10) / 10;
        const norm = s => (s || '').replace(/\s+/g, ' ').trim();
        const textOf = el => norm(el.textContent);
        const visible = el => {
            const r = el.getBoundingClientRect();
            if (r.width <= 0 || r.height <= 0) return false;
            const cs = getComputedStyle(el);
            if (cs.visibility === 'hidden' || cs.display === 'none') return false;
            if (el.checkVisibility && !el.checkVisibility()) return false;
            return true;
        };
        const roleOf = el => {
            const explicit = (el.getAttribute('role') || '').trim().split(/\s+/)[0].toLowerCase();
            if (explicit) return explicit;
            const t = el.localName;
            if (t === 'button') return 'button';
            if (t === 'a') return el.hasAttribute('href') ? 'link' : 'generic';
            if (t === 'input') {
                const ty = (el.getAttribute('type') || 'text').toLowerCase();
                if (['button', 'submit', 'reset', 'image'].includes(ty)) return 'button';
                if (ty === 'checkbox') return 'checkbox';
                if (ty === 'radio') return 'radio';
                if (ty === 'range') return 'slider';
                if (ty === 'number') return 'spinbutton';
                if (ty === 'search') return 'searchbox';
                return el.list ? 'combobox' : 'textbox';
            }
            if (t === 'select') return (el.multiple || el.size > 1) ? 'listbox' : 'combobox';
            if (t === 'textarea') return 'textbox';
            if (t === 'img') return el.getAttribute('alt') === '' ? 'presentation' : 'img';
            if (t === 'svg') return 'img';
            if (t === 'th') return (el.getAttribute('scope') || '').toLowerCase() === 'row' ? 'rowheader' : 'columnheader';
            if (t === 'td') {
                const tbl = el.closest('table,[role=grid],[role=treegrid],[role=table]');
                const tr = tbl ? (tbl.getAttribute('role') || '').toLowerCase() : '';
                return (tr === 'grid' || tr === 'treegrid') ? 'gridcell' : 'cell';
            }
            if (t === 'label') return 'label';
            if (/^h[1-6]$/.test(t)) return 'heading';
            return 'generic';
        };
        const byIds = ids => ids.split(/\s+/).map(id => document.getElementById(id)).filter(x => x).map(textOf).join(' ').trim();
        const labelText = el => {
            if (el.labels && el.labels.length) return norm(Array.from(el.labels).map(textOf).join(' '));
            const w = el.closest('label');
            return w ? textOf(w) : '';
        };
        const fromContent = /^(button|link|heading|cell|gridcell|columnheader|rowheader|tab|menuitem|menuitemcheckbox|menuitemradio|checkbox|radio|switch|option|label|status|alert|generic|tooltip|treeitem)$/;
        const accName = (el, role) => {
            const lb = el.getAttribute('aria-labelledby');
            if (lb) { const t = byIds(lb); if (t) return t; }
            const al = el.getAttribute('aria-label');
            if (al && al.trim()) return norm(al);
            const t = el.localName;
            if (t === 'input' || t === 'select' || t === 'textarea') {
                const ty = (el.getAttribute('type') || '').toLowerCase();
                if (t === 'input' && ['button', 'submit', 'reset'].includes(ty)) return norm(el.value) || (ty === 'submit' ? 'Submit' : ty === 'reset' ? 'Reset' : '');
                if (t === 'input' && ty === 'image') return norm(el.getAttribute('alt') || el.getAttribute('title'));
                const l = labelText(el); if (l) return l;
            }
            if (t === 'img') { const a = el.getAttribute('alt'); if (a && a.trim()) return norm(a); }
            if (t === 'svg') { const ti = el.querySelector('title'); if (ti && textOf(ti)) return textOf(ti); }
            if (fromContent.test(role)) {
                let c = textOf(el);
                if (!c) { const img = el.querySelector('img[alt]'); if (img) c = norm(img.getAttribute('alt')); }
                if (!c) { const ti = el.querySelector('svg title'); if (ti) c = textOf(ti); }
                if (c) return c.slice(0, 120);
            }
            const ti = el.getAttribute('title'); if (ti && ti.trim()) return norm(ti);
            const ph = el.getAttribute('placeholder'); if (ph && ph.trim()) return norm(ph);
            return '';
        };
        const domPath = el => {
            const parts = [];
            for (let n = el; n && n.nodeType === 1 && n !== document.documentElement; n = n.parentElement) {
                const tag = n.localName;
                let i = 1;
                for (let s = n.previousElementSibling; s; s = s.previousElementSibling) if (s.localName === tag) i++;
                parts.unshift(tag + ':nth-of-type(' + i + ')');
            }
            return 'html > ' + parts.join(' > ');
        };
        const lineCount = el => {
            if (!textOf(el)) return 0;
            if (/^(input|select|textarea|img|svg)$/.test(el.localName)) return 0;
            const rg = document.createRange(); rg.selectNodeContents(el);
            const tops = [];
            for (const r of rg.getClientRects()) {
                if (r.width <= 0 || r.height <= 0) continue;
                if (!tops.some(t => Math.abs(t - r.top) < Math.max(2, r.height / 2))) tops.push(r.top);
            }
            return tops.length;
        };
        const clipped = (el, r) => {
            for (let a = el.parentElement; a && a !== document.body && a !== document.documentElement; a = a.parentElement) {
                const cs = getComputedStyle(a);
                if (cs.overflowX === 'visible' && cs.overflowY === 'visible') continue;
                const ar = a.getBoundingClientRect();
                if (r.right > ar.right + 1 || r.bottom > ar.bottom + 1 || r.left < ar.left - 1 || r.top < ar.top - 1) return true;
            }
            return false;
        };
        const styleOf = (el, cs) => {
            const side = s => cs.getPropertyValue('border-' + s + '-width') + ' ' + cs.getPropertyValue('border-' + s + '-style') + ' ' + cs.getPropertyValue('border-' + s + '-color');
            return {
                'color': cs.color, 'background-color': __effBg(el), 'font-size': cs.fontSize, 'font-weight': cs.fontWeight,
                'font-family': __renderedFont(cs.fontFamily),
                'border-top': side('top'), 'border-right': side('right'), 'border-bottom': side('bottom'), 'border-left': side('left'),
                'border-radius': cs.borderRadius, 'padding': cs.padding, 'text-align': cs.textAlign,
                'text-transform': cs.textTransform, 'line-height': cs.lineHeight, 'cursor': cs.cursor, 'opacity': cs.opacity
            };
        };

        // ── candidates (DOM order) ──
        const baseSel = 'button,a[href],input:not([type=hidden]),select,textarea,[role=button],[role=link],[role=checkbox],[role=radio],[role=switch],[role=tab],[role=menuitem],[role=combobox],[role=textbox],[role=searchbox],[role=slider],[role=spinbutton],h1,h2,h3,h4,h5,h6,[role=heading],label,th,[role=columnheader],[role=rowheader],td,[role=gridcell],[role=cell],img,svg[role=img],[role=img],[role=alert],[role=status]';
        const msgSel = '[aria-live],[class*=alert],[class*=message],[class*=validation],[class*=error],[class*=snackbar],[class*=toast],[class*=notification]';
        const cellLike = el => /^(td|th)$/.test(el.localName) || /^(gridcell|cell|columnheader|rowheader)$/.test((el.getAttribute('role') || '').toLowerCase());
        const rowIndexCache = new Map();
        const dataRows = tbl => {
            if (rowIndexCache.has(tbl)) return rowIndexCache.get(tbl);
            const rows = Array.from(tbl.querySelectorAll('tr,[role=row]'))
                .filter(r => r.closest('table,[role=grid],[role=treegrid],[role=table]') === tbl && visible(r)
                    && r.querySelector('td,[role=gridcell],[role=cell]'));
            const m = new Map(); rows.forEach((r, i) => m.set(r, i));
            rowIndexCache.set(tbl, m);
            return m;
        };
        const picked = [];
        const seen = new Set();
        const consider = el => {
            if (seen.has(el)) return;
            seen.add(el);
            if (!visible(el) && !(el.localName === 'input' && getComputedStyle(el).opacity === '0' && el.getBoundingClientRect().width > 0)) return;
            picked.push(el);
        };
        for (const el of document.querySelectorAll(baseSel + ',' + msgSel)) {
            if (picked.length >= opts.maxElements) break;
            if (el.matches(msgSel) && !el.matches(baseSel)) {
                const t = textOf(el);
                if (!t || t.length > 200 || el.querySelector(baseSel + ',' + msgSel)) continue;
            }
            // Repeated grid rows: only elements of the first maxRows data rows (cells and anything inside them).
            const row = el.closest('tr,[role=row]');
            const tbl = row && row.closest('table,[role=grid],[role=treegrid],[role=table]');
            if (row && tbl) {
                const idx = dataRows(tbl).get(row);
                if (idx !== undefined && idx >= opts.maxRows) continue;
            }
            consider(el);
        }
        // A descendant with the same role and text as its nearest inventoried ancestor is the same element for a
        // user (e.g. jqGrid's role=columnheader div inside the th): keep only the ancestor, otherwise it is
        // reported as 'missing on Target' on the side that does not nest.
        {
            const pickedSet = new Set(picked);
            const dupOfAncestor = el => {
                const r = roleOf(el);
                if (r === 'generic') return false;
                for (let a = el.parentElement; a; a = a.parentElement)
                    if (pickedSet.has(a)) return roleOf(a) === r && textOf(a) === textOf(el);
                return false;
            };
            for (let i = picked.length - 1; i >= 0; i--) if (dupOfAncestor(picked[i])) picked.splice(i, 1);
        }
        // scrollWidth also counts decoration the element clips on purpose (e.g. the transformed ripple
        // pseudo-element of Material buttons); only child content past the padding box is a real overflow.
        const contentOverflows = (el, cs) => {
            const rg = document.createRange(); rg.selectNodeContents(el);
            const c = rg.getBoundingClientRect(), b = el.getBoundingClientRect();
            if (c.width === 0 && c.height === 0) return false;
            const bl = parseFloat(cs.borderLeftWidth) || 0, br = parseFloat(cs.borderRightWidth) || 0;
            return c.right > b.right - br + 1 || c.left < b.left + bl - 1;
        };

        // ── containers: nearest ancestor holding >= 2 inventoried elements ──
        const counts = new Map();
        for (const el of picked) for (let a = el.parentElement; a; a = a.parentElement) counts.set(a, (counts.get(a) || 0) + 1);
        const containerOf = el => { for (let a = el.parentElement; a; a = a.parentElement) if ((counts.get(a) || 0) >= 2) return a; return document.body; };
        const pathCache = new Map();
        const pathOf = el => { if (!pathCache.has(el)) pathCache.set(el, domPath(el)); return pathCache.get(el); };
        const orderIn = new Map();

        const elements = [];
        const a11y = [];
        picked.forEach((el, index) => {
            const r = el.getBoundingClientRect();
            const cs = getComputedStyle(el);
            const role = roleOf(el);
            const name = accName(el, role);
            const container = containerOf(el);
            const order = orderIn.get(container) || 0; orderIn.set(container, order + 1);
            const interactive = __interactive(el);
            const disabled = !!(el.disabled || el.getAttribute('aria-disabled') === 'true' || (el.closest && el.closest('fieldset[disabled]')));
            const item = {
                index, role, name,
                label: (el.localName === 'input' || el.localName === 'select' || el.localName === 'textarea') ? labelText(el) : '',
                placeholder: norm(el.getAttribute('placeholder')),
                inputType: el.localName === 'input' ? (el.getAttribute('type') || 'text').toLowerCase() : '',
                tag: el.localName,
                text: textOf(el).slice(0, 80),
                box: { x: r1(r.left + sx), y: r1(r.top + sy), w: r1(r.width), h: r1(r.height) },
                domPath: pathOf(el),
                container: pathOf(container),
                order,
                interactive, disabled,
                mapName: el.getAttribute('" + MapAttribute + @"') || null,
                styles: styleOf(el, cs),
                states: {},
                overflowX: el.clientWidth > 0 && el.scrollWidth > el.clientWidth + 1 && contentOverflows(el, cs),
                ellipsis: cs.textOverflow === 'ellipsis' && el.clientWidth > 0 && el.scrollWidth > el.clientWidth,
                lines: lineCount(el),
                clipped: clipped(el, r)
            };
            const row = el.closest('tr,[role=row]');
            const tbl = el.closest('table,[role=grid],[role=treegrid],[role=table]');
            if (row && tbl && cellLike(el)) {
                const idx = dataRows(tbl).get(row);
                if (idx !== undefined) item.rowIndex = idx;
                item.colIndex = Array.from(row.children).filter(c => visible(c)).indexOf(el);
            }
            if (el.localName === 'label' && el.control && visible(el.control)) item.labelFor = pathOf(el.control);
            if (disabled) {
                const s = styleOf(el, cs);
                for (const k of ['color', 'background-color', 'cursor', 'opacity']) item.states['disabled.' + k] = s[k];
            }
            if (opts.health && opts.a11y) {
                const what = el.localName + (el.id ? '#' + el.id : '') + (item.text ? ' ""' + item.text.slice(0, 40) + '""' : '');
                if (interactive && !name && !/^(generic|presentation|none)$/.test(role)) a11y.push(role + ' without an accessible name: ' + what);
                if (el.localName === 'img' && !el.hasAttribute('alt')) a11y.push('image without alt text: ' + (el.getAttribute('src') || what));
            }
            elements.push(item);
        });

        const health = {};
        if (opts.health) {
            if (opts.brokenImages)
                health.brokenImages = Array.from(document.images).filter(i => i.complete && i.naturalWidth === 0 && i.getAttribute('src') && visible(i))
                    .map(i => i.currentSrc || i.getAttribute('src'));
            if (opts.emptySections) {
                const empty = [];
                const isHeading = n => n.nodeType === 1 && (/^h[1-6]$/.test(n.localName) || (n.getAttribute('role') || '') === 'heading');
                for (const h of document.querySelectorAll('h1,h2,h3,h4,h5,h6,[role=heading]')) {
                    if (!visible(h) || !textOf(h)) continue;
                    const w = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
                    w.currentNode = h;
                    let n = w.nextNode();
                    while (n && h.contains(n)) n = w.nextNode();
                    let found = false, steps = 0;
                    for (; n && steps < 2000; n = w.nextNode(), steps++) {
                        if (isHeading(n)) break;
                        if (n.nodeType === 3) {
                            if (n.textContent.trim() && n.parentElement && visible(n.parentElement)) { found = true; break; }
                        } else if (/^(input|select|textarea|button|img|svg|canvas|iframe|video|table)$/.test(n.localName) && visible(n)) { found = true; break; }
                    }
                    if (!found) empty.push('heading ""' + textOf(h).slice(0, 80) + '"" has no content below it');
                }
                for (const s of document.querySelectorAll('section,[role=region]')) {
                    if (!visible(s)) continue;
                    if (!norm(s.innerText) && !s.querySelector('input,select,textarea,button,img,svg,canvas,iframe,video'))
                        empty.push(s.localName + (s.id ? '#' + s.id : '') + ' is visible but empty');
                }
                health.emptySections = empty;
            }
            if (opts.a11y) health.a11y = Array.from(new Set(a11y));
        }
        const de = document.documentElement;
        return JSON.stringify({
            viewport: { width: window.innerWidth, height: window.innerHeight, scrollWidth: de.scrollWidth, scrollHeight: de.scrollHeight },
            elements, health
        });
    }";
}

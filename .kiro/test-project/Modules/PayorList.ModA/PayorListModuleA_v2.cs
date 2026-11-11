// plan-revision: 1; steps=17
using Microsoft.Playwright;
using NUnit.Framework;
using MigrationTests.Playwright;
using MigrationTests.Playwright.Helpers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MigrationTests.Playwright.Tests;

/// <summary>
/// TARGET MODULE A — PayorList: Load the page and explore the grid (sort &amp; pagination).
///
/// Same human workflow as Reference Module A, run against the TARGET system:
///   Blazor Server + MudBlazor MudDataGrid at /Pages/PayorList (port 5200).
///
/// Assertions use the EXACT confirmed Phase 1 reference baseline values (not "non-empty"/
/// "contains"). Any mismatch is a FINDING documenting a reference-vs-target difference — the
/// assertion is NOT weakened to make the test pass.
///
/// The heading "Payor Information Management" is read from the MudAppBar h6 (not h1).
/// MudDataGridPager has no direct page-number box: Step9 types into one when present and
/// otherwise reaches page 2 with Next, so pager.page is reported as a gap, not hidden.
///
/// 17 steps (Step1..Step17, plus NAV) mirroring ReferencePayorListModuleA_v2.
///
/// Run: APP_TEST_MODE=validate dotnet test --filter "Name~PayorList_ModA" --no-build
/// </summary>
[TestFixture]
public class PayorListModuleA_v2 : TestBase
{
    // ─────────────────────────────────────────────────────────────────────
    // MudBlazor MudDataGrid selectors (confirmed via live DOM probe).
    // Grid = table.mud-table-root; rows = tbody tr.mud-table-row; cells = td (by column index).
    // Pager caption class is shared: [0] "Rows per page:", [last] the range "1-5 of 15".
    // ─────────────────────────────────────────────────────────────────────
    private const string GridRowsSel = "tbody tr.mud-table-row";
    private const string PagerRangeSel = ".mud-table-pagination-caption";       // last one = range text
    private const string NextSel = "button[aria-label='Next Page']";
    private const string PrevSel = "button[aria-label='Previous Page']";
    private const string FirstSel = "button[aria-label='First Page']";
    private const string LastSel = "button[aria-label='Last Page']";
    private const string PageSizeSelectSel = ".mud-table-pagination .mud-select-input";
    private const string PageInputSel = "input[aria-label='Page No.']";  // direct page box (pager-goto)
    private const string NameHeaderSel = "th .sortable-column-header";          // filter by text "Payor Name"
    private const string CodeHeaderSel = "th .sortable-column-header";          // filter by text "Code"
    private const string SaveBtnText = "Save All";
    private const string CancelBtnText = "Cancel";

    // Reference baseline (Phase 1 confirmed) — EXACT expected values.
    private const string RefHeading = "Payor Information Management";
    private const string RefColumnMap = "0:ID|1:Payor Name|2:Code|3:Category|4:Effective Date|5:Status";
    private const string RefPage1Pager = "View 1 - 10 of 15";
    private const string RefPage2Pager = "View 11 - 15 of 15";
    private const string RefSize15Pager = "View 1 - 15 of 15";

    #region Navigation

    private async Task<bool> NavigateToPage()
    {
        await Page.GotoAsync($"{Settings.BaseUrl}/Pages/PayorList");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AssertNotRedirectedToAuth();

        // Wait for MudDataGrid rows (Blazor Server hydration + SignalR data load).
        try
        {
            await Page.WaitForSelectorAsync(GridRowsSel,
                new() { Timeout = 20000, State = WaitForSelectorState.Visible });
            await Page.WaitForTimeoutAsync(1500);
        }
        catch (TimeoutException)
        {
            await CaptureDiagnosticHtmlAsync(GridRowsSel, "MudDataGrid rows never appeared");
            return false;
        }

        return Page.Url.Contains("PayorList", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Grid read helpers

    private async Task<int> GetRowCount() => await Page.Locator(GridRowsSel).CountAsync();

    /// <summary>MudDataGrid has no per-row DOM id; the ID column is cell index 0. Returns ids in display order.</summary>
    private async Task<List<string>> GetRowIds()
    {
        var ids = new List<string>();
        var rows = Page.Locator(GridRowsSel);
        int n = await rows.CountAsync();
        for (int i = 0; i < n; i++)
        {
            var t = (await rows.Nth(i).Locator("td").Nth(0).TextContentAsync())?.Trim() ?? "";
            if (!string.IsNullOrEmpty(t)) ids.Add(t);
        }
        return ids;
    }

    /// <summary>Reads a text column by cell index for the first N rows, in display order.</summary>
    private async Task<List<string>> GetColumnSequence(int colIndex, int max = 10)
    {
        var vals = new List<string>();
        var rows = Page.Locator(GridRowsSel);
        int n = await rows.CountAsync();
        for (int i = 0; i < Math.Min(n, max); i++)
        {
            var t = (await rows.Nth(i).Locator("td").Nth(colIndex).TextContentAsync())?.Trim() ?? "";
            vals.Add(t);
        }
        return vals;
    }

    private async Task<string> GetFirstRowId() => (await GetRowIds()).FirstOrDefault() ?? "";
    private async Task<string> GetFirstRowName() => (await GetColumnSequence(1, 1)).FirstOrDefault() ?? "";
    private async Task<string> GetFirstRowCode() => (await GetColumnSequence(2, 1)).FirstOrDefault() ?? "";

    /// <summary>
    /// MudDataGridPager renders two elements with class .mud-table-pagination-caption:
    /// [0] = "Rows per page:" label, [last] = the range text "1-5 of 15".
    /// </summary>
    private async Task<string> GetPagerInfo()
    {
        var caps = Page.Locator(PagerRangeSel);
        int n = await caps.CountAsync();
        if (n == 0) return "";
        return (await caps.Nth(n - 1).TextContentAsync())?.Trim() ?? "";
    }

    private async Task<string> GetHeading()
    {
        var el = Page.Locator("h6.mud-typography-h6, .mud-appbar .mud-typography").First;
        return await el.CountAsync() > 0 ? (await el.TextContentAsync())?.Trim() ?? "" : "";
    }

    /// <summary>Column header labels in order → "0:ID|1:Payor Name|...".</summary>
    private async Task<string> GetColumnMap()
    {
        var labels = await Page.EvaluateAsync<string[]>(@"() => {
            const ths = Array.from(document.querySelectorAll('table.mud-table-root thead th'));
            return ths.map(th => {
                const lbl = th.querySelector('.sortable-column-header') || th.querySelector('.column-header');
                return lbl ? lbl.textContent.trim() : th.textContent.trim();
            });
        }");
        var parts = new List<string>();
        for (int i = 0; i < labels.Length; i++) parts.Add($"{i}:{labels[i]}");
        return string.Join("|", parts);
    }

    /// <summary>MudDataGrid sort state for a column header: "none" | "asc" | "desc".</summary>
    private async Task<string> GetSortIndicator(string headerText)
    {
        return await Page.EvaluateAsync<string>(@"(headerText) => {
            const ths = Array.from(document.querySelectorAll('table.mud-table-root thead th'));
            const th = ths.find(t => {
                const lbl = t.querySelector('.sortable-column-header');
                return lbl && lbl.textContent.trim() === headerText;
            });
            if (!th) return 'none';
            const icon = th.querySelector('.sort-direction-icon');
            if (!icon) return 'none';
            const cls = icon.className || '';
            // MudBlazor applies mud-direction-asc / mud-direction-desc when active.
            if (cls.includes('mud-direction-asc')) return 'asc';
            if (cls.includes('mud-direction-desc')) return 'desc';
            return 'none';
        }", headerText);
    }

    private async Task<bool> IsButtonEnabled(string buttonText)
    {
        // Action bar buttons are plain <button> elements (#btnSaveAll / #btnCancelAll).
        var el = Page.Locator($".action-bar button:has-text('{buttonText}')").First;
        if (await el.CountAsync() == 0) return false;
        return await el.IsEnabledAsync();
    }

    /// <summary>Total record count "Z" from the pager text "View X - Y of Z".</summary>
    private static string ParseTotal(string pager)
    {
        var m = Regex.Match(pager, @"of\s+([\d,]+)");
        return m.Success ? m.Groups[1].Value.Replace(",", "") : "";
    }

    private async Task<string> GetPagerTotal() => ParseTotal(await GetPagerInfo());

    /// <summary>Rows-per-page value shown by the MudDataGridPager select.</summary>
    private async Task<string> GetPageSize()
    {
        return await Page.EvaluateAsync<string>(@"() => {
            const pg = document.querySelector('.mud-table-pagination');
            if (!pg) return '';
            const inp = pg.querySelector('.mud-select input');
            if (inp && inp.value) return inp.value.trim();
            const div = pg.querySelector('.mud-select-input');
            return div ? (div.value || div.textContent || '').trim() : '';
        }");
    }

    /// <summary>Direct page-number box value; empty when the pager has none (MudDataGridPager default).</summary>
    private async Task<string> GetCurrentPage()
    {
        var box = Page.Locator(PageInputSel).First;
        return await box.CountAsync() > 0 ? await box.InputValueAsync() : "";
    }

    /// <summary>Unsaved-changes counter (#changeCount in the action bar); empty when the Target renders no counter.</summary>
    private async Task<string> GetChangeCount()
    {
        var el = Page.Locator("#changeCount").First;
        return await el.CountAsync() > 0 ? (await el.TextContentAsync())?.Trim() ?? "" : "";
    }

    /// <summary>Visible text of column <paramref name="colIndex"/> for every data row, in display order.</summary>
    private async Task<List<string>> GetColumnByIndex(int colIndex)
    {
        var vals = await Page.EvaluateAsync<string[]>(@"([sel, idx]) =>
            Array.from(document.querySelectorAll(sel)).map(r => {
                const td = r.querySelectorAll('td')[idx];
                return td ? td.textContent.trim() : '';
            })", new object[] { GridRowsSel, colIndex });
        return vals.ToList();
    }

    private async Task<string> GetSeq(int colIndex) => string.Join(",", await GetColumnByIndex(colIndex));

    #endregion

    #region Sort-order checks (full visible column sequence)

    private static bool IsOrdered(List<string> seq, Comparison<string> cmp, bool descending = false)
    {
        if (seq.Count == 0) return false;
        for (int i = 1; i < seq.Count; i++)
        {
            var c = cmp(seq[i - 1], seq[i]);
            if (descending ? c < 0 : c > 0) return false;
        }
        return true;
    }

    private static int CmpText(string a, string b) =>
        string.Compare(a, b, StringComparison.InvariantCultureIgnoreCase);

    private static DateTime ParseDate(string s) =>
        DateTime.TryParseExact(s, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : DateTime.MinValue; // "—" (no date) sorts first

    private static int CmpDate(string a, string b) => ParseDate(a).CompareTo(ParseDate(b));

    // Status sorts on the boolean: Inactive (false) before Active (true).
    private static int StatusRank(string s) => s.Contains("Inactive", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    private static int CmpStatus(string a, string b) => StatusRank(a).CompareTo(StatusRank(b));

    #endregion

    #region Grid action helpers

    private async Task ClickPagerNav(string selector)
    {
        var el = Page.Locator(selector).First;
        await el.ScrollIntoViewIfNeededAsync();
        await el.ClickAsync();
        await Page.WaitForTimeoutAsync(3000);
    }

    private async Task SetPageSize(string value)
    {
        await Page.Locator(PageSizeSelectSel).First.ClickAsync();
        await Page.WaitForTimeoutAsync(600);
        // Options render in a popover list; match exact text.
        await Page.Locator(".mud-list-item").Filter(new() { HasTextString = value }).First.ClickAsync();
        await Page.WaitForTimeoutAsync(3000);
    }

    private async Task ClickSortByText(string headerText)
    {
        // Exact header label match ("ID" must not match other labels).
        var exact = new Regex("^\\s*" + Regex.Escape(headerText) + "\\s*$");
        await Page.Locator(NameHeaderSel).Filter(new() { HasTextRegex = exact }).First.ClickAsync();
        await Page.WaitForTimeoutAsync(3000);
    }

    /// <summary>Type the page number into the direct page box and press Enter; when the pager
    /// has no such box, reach the page with Next (the pager.page check then reports the gap).</summary>
    private async Task SetDirectPage(int page)
    {
        var box = Page.Locator(PageInputSel).First;
        if (await box.CountAsync() > 0)
        {
            await box.FillAsync(page.ToString());
            await box.PressAsync("Enter");
            await Page.WaitForTimeoutAsync(3000);
            return;
        }
        _log?.Invoke("Step9: Target pager has no direct page-number box; using Next to reach page " + page);
        await ClickPagerNav(NextSel);
    }

    private Action<string>? _log;

    private async Task ResetToPage1()
    {
        try
        {
            var first = Page.Locator(FirstSel).First;
            if (await first.CountAsync() > 0 && await first.IsEnabledAsync())
                await ClickPagerNav(FirstSel);
        }
        catch { }
    }

    #endregion

    [Test]
    public async Task PayorList_ModA()
    {
        var runner = new ModuleTestRunner("PayorList", "A", "Target", 1);
        _log = runner.Log;

        runner.Log("══════════════════════════════════════════════");
        runner.Log("TARGET MODULE A — PayorList: Load & Explore Grid (Blazor/MudDataGrid)");
        runner.Log("══════════════════════════════════════════════");

        if (!await NavigateToPage())
        {
            runner.StepFail("NAV", "Navigation to target PayorList failed (grid rows never rendered)");
            await runner.GenerateReport();
            MarkIssueFound("Navigation failed — target grid did not load");
            return;
        }
        runner.StepPass("NAV", "Opened target /Pages/PayorList (MudDataGrid) and data rows rendered.");

        // ════════════════════════════════════════════════════════════════
        // STEP 1 — Page load (S0): heading, grid, rowCount, pager, total, first row, counter, buttons disabled
        // Expected = EXACT reference baseline.
        // ════════════════════════════════════════════════════════════════
        await runner.ExecuteStep("Step1", Page, "Load the page and verify the initial grid (S0)",
            action: async () => { await Task.CompletedTask; },
            assertions: async () =>
            {
                var heading = await GetHeading();
                runner.Capture("S0", "heading.text", heading);
                runner.AssertEquals("Step1", "heading.text", heading, RefHeading);

                var gridVisible = await Page.Locator("table.mud-table-root").First.IsVisibleAsync();
                runner.Assert("Step1", "grid.visible", gridVisible, "Grid is visible", "Grid NOT visible");

                var rowCount = await GetRowCount();
                runner.Capture("S0", "grid.rowCount", rowCount.ToString());
                runner.AssertEquals("Step1", "grid.rowCount", rowCount.ToString(), "10");

                var pager = await GetPagerInfo();
                runner.Capture("S0", "pager.info", pager);
                runner.AssertEquals("Step1", "pager.info", pager, RefPage1Pager);

                var total = ParseTotal(pager);
                runner.Capture("S0", "pager.total", total);
                runner.AssertEquals("Step1", "pager.total", total, "15");

                var firstId = await GetFirstRowId();
                var firstName = await GetFirstRowName();
                runner.Capture("S0", "grid.row[0].id", firstId);
                runner.Capture("S0", "grid.row[0].name", firstName);
                runner.AssertEquals("Step1", "grid.row[0].id", firstId, "1");
                runner.AssertEquals("Step1", "grid.row[0].name", firstName, "Acme Health Plans");

                var changeCount = await GetChangeCount();
                runner.Capture("S0", "changeCount.text", changeCount);
                runner.AssertEquals("Step1", "changeCount.text", changeCount, "No unsaved changes");

                var saveEnabled = (await IsButtonEnabled(SaveBtnText)).ToString().ToLower();
                var cancelEnabled = (await IsButtonEnabled(CancelBtnText)).ToString().ToLower();
                runner.Capture("S0", "saveButton.enabled", saveEnabled);
                runner.Capture("S0", "cancelButton.enabled", cancelEnabled);
                runner.AssertEquals("Step1", "saveButton.enabled", saveEnabled, "false");
                runner.AssertEquals("Step1", "cancelButton.enabled", cancelEnabled, "false");
            },
            gridSelector: "table.mud-table-root tbody");

        await runner.SaveSnapshot(Page, "S0", "page_load", "table.mud-table-root");

        // CSS capture — page_load (S0), after Step1 per css-capture-plan.md
        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "target", "page_load"); } catch { }

        // Step1 baseline values (Target runtime) used as "before" values below.
        var baseTotal = await GetPagerTotal();
        var baseRowIds = string.Join(",", await GetRowIds());

        // ════════════════════════════════════════════════════════════════
        // STEP 2 — Capture column map (header text → index)
        // ════════════════════════════════════════════════════════════════
        var s2RowsBefore = (await GetRowCount()).ToString();
        var s2PagerBefore = await GetPagerInfo();
        await runner.ExecuteStep("Step2", Page, "Read the column headers and capture the column map",
            action: async () => { await Task.CompletedTask; },
            assertions: async () =>
            {
                var map = await GetColumnMap();
                runner.Capture("S0", "grid.columnMap", map);
                runner.AssertEquals("Step2", "grid.columnMap", map, RefColumnMap);

                var rowCount = await GetRowCount();
                runner.AssertUnchanged("Step2", "grid.rowCount", s2RowsBefore, rowCount.ToString());
                var pager = await GetPagerInfo();
                runner.AssertUnchanged("Step2", "pager.info", s2PagerBefore, pager);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 3 — Next page (1 → 2). Expected reference values for page 2.
        // ════════════════════════════════════════════════════════════════
        var s3PagerBefore = await GetPagerInfo();
        var s3IdBefore = await GetFirstRowId();
        var s3RowIdsBefore = string.Join(",", await GetRowIds());
        var s3SaveBefore = (await IsButtonEnabled(SaveBtnText)).ToString().ToLower();
        await runner.ExecuteStep("Step3", Page, "Click Next page (1 -> 2)",
            action: async () => await ClickPagerNav(NextSel),
            assertions: async () =>
            {
                var pager = await GetPagerInfo();
                var firstId = await GetFirstRowId();
                var rowIds = string.Join(",", await GetRowIds());
                var rowCount = await GetRowCount();
                runner.Capture("S_p2", "pager.info", pager);
                runner.Capture("S_p2", "grid.row[0].id", firstId);
                runner.Capture("S_p2", "grid.rowIds", rowIds);
                runner.Capture("S_p2", "grid.rowCount", rowCount.ToString());
                runner.AssertChanged("Step3", "pager.info", s3PagerBefore, pager);
                runner.AssertEquals("Step3", "pager.info", pager, RefPage2Pager);
                runner.AssertChanged("Step3", "grid.row[0].id", s3IdBefore, firstId);
                runner.AssertEquals("Step3", "grid.row[0].id", firstId, "11");
                runner.AssertChanged("Step3", "grid.rowIds", s3RowIdsBefore, rowIds);
                runner.AssertEquals("Step3", "grid.rowCount", rowCount.ToString(), "5");
                runner.AssertUnchanged("Step3", "pager.total", baseTotal, ParseTotal(pager));
                runner.AssertUnchanged("Step3", "saveButton.enabled",
                    s3SaveBefore, (await IsButtonEnabled(SaveBtnText)).ToString().ToLower());
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 4 — Previous page (2 → 1)
        // ════════════════════════════════════════════════════════════════
        var s4PagerBefore = await GetPagerInfo();
        var s4IdBefore = await GetFirstRowId();
        await runner.ExecuteStep("Step4", Page, "Click Previous page (2 -> 1)",
            action: async () => await ClickPagerNav(PrevSel),
            assertions: async () =>
            {
                var pager = await GetPagerInfo();
                var firstId = await GetFirstRowId();
                var rowCount = await GetRowCount();
                runner.Capture("S_p1b", "pager.info", pager);
                runner.AssertChanged("Step4", "pager.info", s4PagerBefore, pager);
                runner.AssertEquals("Step4", "pager.info", pager, RefPage1Pager);
                runner.AssertChanged("Step4", "grid.row[0].id", s4IdBefore, firstId);
                runner.AssertEquals("Step4", "grid.row[0].id", firstId, "1");
                runner.AssertEquals("Step4", "grid.rowCount", rowCount.ToString(), "10");
                runner.AssertUnchanged("Step4", "pager.total", baseTotal, ParseTotal(pager));
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 5 — Last page
        // ════════════════════════════════════════════════════════════════
        var s5PagerBefore = await GetPagerInfo();
        await runner.ExecuteStep("Step5", Page, "Click Last page",
            action: async () => await ClickPagerNav(LastSel),
            assertions: async () =>
            {
                var pager = await GetPagerInfo();
                var firstId = await GetFirstRowId();
                var rowCount = await GetRowCount();
                runner.Capture("S_last", "pager.info", pager);
                runner.Capture("S_last", "grid.rowCount", rowCount.ToString());
                runner.AssertChanged("Step5", "pager.info", s5PagerBefore, pager);
                runner.AssertEquals("Step5", "pager.info", pager, RefPage2Pager);
                runner.AssertEquals("Step5", "grid.row[0].id", firstId, "11");
                runner.AssertEquals("Step5", "grid.rowCount", rowCount.ToString(), "5");
                runner.AssertUnchanged("Step5", "pager.total", baseTotal, ParseTotal(pager));
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 6 — First page (last → 1)
        // ════════════════════════════════════════════════════════════════
        var s6PagerBefore = await GetPagerInfo();
        await runner.ExecuteStep("Step6", Page, "Click First page (last -> 1)",
            action: async () => await ClickPagerNav(FirstSel),
            assertions: async () =>
            {
                var pager = await GetPagerInfo();
                var firstId = await GetFirstRowId();
                runner.Capture("S_first", "pager.info", pager);
                runner.AssertChanged("Step6", "pager.info", s6PagerBefore, pager);
                runner.AssertEquals("Step6", "pager.info", pager, RefPage1Pager);
                runner.AssertEquals("Step6", "grid.row[0].id", firstId, "1");
                runner.AssertUnchanged("Step6", "pager.total", baseTotal, ParseTotal(pager));
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 7 — Page size → 15
        // ════════════════════════════════════════════════════════════════
        var s7RowsBefore = (await GetRowCount()).ToString();
        var s7PagerBefore = await GetPagerInfo();
        var s7IdBefore = await GetFirstRowId();
        var s7SizeBefore = await GetPageSize();
        await runner.ExecuteStep("Step7", Page, "Change page size 10 -> 15",
            action: async () => await SetPageSize("15"),
            assertions: async () =>
            {
                var size = await GetPageSize();
                var rowCount = await GetRowCount();
                var pager = await GetPagerInfo();
                var firstId = await GetFirstRowId();
                runner.Capture("S_size15", "pager.pageSize", size);
                runner.Capture("S_size15", "grid.rowCount", rowCount.ToString());
                runner.Capture("S_size15", "pager.info", pager);
                runner.AssertChanged("Step7", "pager.pageSize", s7SizeBefore, size);
                runner.AssertEquals("Step7", "pager.pageSize", size, "15");
                runner.AssertChanged("Step7", "grid.rowCount", s7RowsBefore, rowCount.ToString());
                runner.AssertEquals("Step7", "grid.rowCount", rowCount.ToString(), "15");
                runner.AssertChanged("Step7", "pager.info", s7PagerBefore, pager);
                runner.AssertEquals("Step7", "pager.info", pager, RefSize15Pager);
                runner.AssertUnchanged("Step7", "pager.total", baseTotal, ParseTotal(pager));
                runner.AssertUnchanged("Step7", "grid.row[0].id", s7IdBefore, firstId);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 8 — Page size → 10 (restore to reference default)
        // ════════════════════════════════════════════════════════════════
        var s8RowsBefore = (await GetRowCount()).ToString();
        var s8PagerBefore = await GetPagerInfo();
        var s8SizeBefore = await GetPageSize();
        await runner.ExecuteStep("Step8", Page, "Restore page size 15 -> 10",
            action: async () => await SetPageSize("10"),
            assertions: async () =>
            {
                var size = await GetPageSize();
                var rowCount = await GetRowCount();
                var pager = await GetPagerInfo();
                runner.Capture("S_size10", "pager.pageSize", size);
                runner.Capture("S_size10", "grid.rowCount", rowCount.ToString());
                runner.AssertChanged("Step8", "pager.pageSize", s8SizeBefore, size);
                runner.AssertEquals("Step8", "pager.pageSize", size, "10");
                runner.AssertChanged("Step8", "grid.rowCount", s8RowsBefore, rowCount.ToString());
                runner.AssertEquals("Step8", "grid.rowCount", rowCount.ToString(), "10");
                runner.AssertChanged("Step8", "pager.info", s8PagerBefore, pager);
                runner.AssertEquals("Step8", "pager.info", pager, RefPage1Pager);
                runner.AssertUnchanged("Step8", "pager.total", baseTotal, ParseTotal(pager));
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 9 — Direct page input → page 2
        // MudDataGridPager has no direct page-number box: SetDirectPage falls back to Next so the
        // later steps still run; pager.page then reports the missing box as a gap.
        // ════════════════════════════════════════════════════════════════
        var s9PagerBefore = await GetPagerInfo();
        var s9PageBefore = await GetCurrentPage();
        var s9SizeBefore = await GetPageSize();
        await runner.ExecuteStep("Step9", Page, "Type page 2 into the direct page box and press Enter",
            action: async () => await SetDirectPage(2),
            assertions: async () =>
            {
                var pageNo = await GetCurrentPage();
                var pager = await GetPagerInfo();
                var firstId = await GetFirstRowId();
                runner.Capture("S_direct2", "pager.page", pageNo);
                runner.Capture("S_direct2", "pager.info", pager);
                runner.AssertChanged("Step9", "pager.page", s9PageBefore, pageNo);
                runner.AssertEquals("Step9", "pager.page", pageNo, "2");
                runner.AssertChanged("Step9", "pager.info", s9PagerBefore, pager);
                runner.AssertEquals("Step9", "pager.info", pager, RefPage2Pager);
                runner.AssertEquals("Step9", "grid.row[0].id", firstId, "11");
                runner.AssertUnchanged("Step9", "pager.total", baseTotal, ParseTotal(pager));
                runner.AssertUnchanged("Step9", "pager.pageSize", s9SizeBefore, await GetPageSize());
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 10 — First page (restore page 1 at the default size)
        // ════════════════════════════════════════════════════════════════
        var s10PagerBefore = await GetPagerInfo();
        var s10IdBefore = await GetFirstRowId();
        var s10SizeBefore = await GetPageSize();
        await runner.ExecuteStep("Step10", Page, "Click First page to restore page 1 at the default size",
            action: async () => await ClickPagerNav(FirstSel),
            assertions: async () =>
            {
                var pager = await GetPagerInfo();
                var firstId = await GetFirstRowId();
                runner.Capture("S_first2", "pager.info", pager);
                runner.AssertChanged("Step10", "pager.info", s10PagerBefore, pager);
                runner.AssertEquals("Step10", "pager.info", pager, RefPage1Pager);
                runner.AssertChanged("Step10", "grid.row[0].id", s10IdBefore, firstId);
                runner.AssertEquals("Step10", "grid.row[0].id", firstId, "1");
                runner.AssertUnchanged("Step10", "pager.total", baseTotal, ParseTotal(pager));
                runner.AssertUnchanged("Step10", "pager.pageSize", s10SizeBefore, await GetPageSize());
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 11 — Sort Payor Name ASC (S3)
        // ════════════════════════════════════════════════════════════════
        var nameSeqBefore = await GetSeq(1);
        var s11RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step11", Page, "Sort by Payor Name ascending (click header once)",
            action: async () => await ClickSortByText("Payor Name"),
            assertions: async () =>
            {
                var ind = await GetSortIndicator("Payor Name");
                var seqList = await GetColumnByIndex(1);
                var seq = string.Join(",", seqList);
                var rowCount = (await GetRowCount()).ToString();
                runner.Capture("S_sortNameAsc", "sort.indicator[Payor Name]", ind);
                runner.Capture("S_sortNameAsc", "grid.column[Payor Name].sequence", seq);
                runner.AssertEquals("Step11", "sort.indicator[Payor Name]", ind, "asc");
                runner.AssertChanged("Step11", "grid.column[Payor Name].sequence", nameSeqBefore, seq);
                runner.Assert("Step11", "grid.column[Payor Name].sequence", IsOrdered(seqList, CmpText),
                    $"Payor Names ascending: {seq}", $"Payor Names NOT ascending: {seq}");
                runner.AssertUnchanged("Step11", "grid.rowCount", s11RowsBefore, rowCount);
                runner.AssertUnchanged("Step11", "pager.total", baseTotal, await GetPagerTotal());
            },
            gridSelector: "table.mud-table-root tbody");

        // CSS capture — sorted (after Step11, Payor Name ascending)
        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "target", "sorted"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 12 — Sort Payor Name DESC (toggle)
        // ════════════════════════════════════════════════════════════════
        var s12NameBefore = await GetFirstRowName();
        var s12RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step12", Page, "Sort by Payor Name descending (click header again)",
            action: async () => await ClickSortByText("Payor Name"),
            assertions: async () =>
            {
                var ind = await GetSortIndicator("Payor Name");
                var seqList = await GetColumnByIndex(1);
                var seq = string.Join(",", seqList);
                var firstName = await GetFirstRowName();
                var rowCount = (await GetRowCount()).ToString();
                runner.Capture("S_sortNameDesc", "sort.indicator[Payor Name]", ind);
                runner.Capture("S_sortNameDesc", "grid.column[Payor Name].sequence", seq);
                runner.Capture("S_sortNameDesc", "grid.row[0].name", firstName);
                runner.AssertEquals("Step12", "sort.indicator[Payor Name]", ind, "desc");
                runner.Assert("Step12", "grid.column[Payor Name].sequence", IsOrdered(seqList, CmpText, descending: true),
                    $"Payor Names descending: {seq}", $"Payor Names NOT descending: {seq}");
                runner.AssertChanged("Step12", "grid.row[0].name", s12NameBefore, firstName);
                runner.AssertEquals("Step12", "grid.row[0].name", firstName, "Willowbend Health Plans");
                runner.AssertUnchanged("Step12", "grid.rowCount", s12RowsBefore, rowCount);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 13 — Sort Code (switch sort column)
        // ════════════════════════════════════════════════════════════════
        var s13RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step13", Page, "Sort by Code (switch sort column)",
            action: async () => await ClickSortByText("Code"),
            assertions: async () =>
            {
                var nameInd = await GetSortIndicator("Payor Name");
                var codeInd = await GetSortIndicator("Code");
                var seqList = await GetColumnByIndex(2);
                var seq = string.Join(",", seqList);
                var firstCode = await GetFirstRowCode();
                var rowCount = (await GetRowCount()).ToString();
                runner.Capture("S_sortCode", "sort.indicator[Payor Name]", nameInd);
                runner.Capture("S_sortCode", "sort.indicator[Code]", codeInd);
                runner.Capture("S_sortCode", "grid.column[Code].sequence", seq);
                runner.AssertEquals("Step13", "sort.indicator[Payor Name]", nameInd, "none");
                runner.AssertEquals("Step13", "sort.indicator[Code]", codeInd, "asc");
                runner.Assert("Step13", "grid.column[Code].sequence", IsOrdered(seqList, CmpText),
                    $"Codes ascending: {seq}", $"Codes NOT ascending: {seq}");
                runner.AssertEquals("Step13", "grid.row[0].code", firstCode, "ACM001");
                runner.AssertUnchanged("Step13", "grid.rowCount", s13RowsBefore, rowCount);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 14 — Sort Category
        // ════════════════════════════════════════════════════════════════
        var s14RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step14", Page, "Sort by Category",
            action: async () => await ClickSortByText("Category"),
            assertions: async () =>
            {
                var codeInd = await GetSortIndicator("Code");
                var catInd = await GetSortIndicator("Category");
                var seqList = await GetColumnByIndex(3);
                var seq = string.Join(",", seqList);
                var rowCount = (await GetRowCount()).ToString();
                runner.Capture("S_sortCategory", "sort.indicator[Code]", codeInd);
                runner.Capture("S_sortCategory", "sort.indicator[Category]", catInd);
                runner.Capture("S_sortCategory", "grid.column[Category].sequence", seq);
                runner.AssertEquals("Step14", "sort.indicator[Code]", codeInd, "none");
                runner.AssertEquals("Step14", "sort.indicator[Category]", catInd, "asc");
                runner.Assert("Step14", "grid.column[Category].sequence", IsOrdered(seqList, CmpText),
                    $"Categories ascending: {seq}", $"Categories NOT ascending: {seq}");
                runner.AssertUnchanged("Step14", "grid.rowCount", s14RowsBefore, rowCount);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 15 — Sort Effective Date
        // ════════════════════════════════════════════════════════════════
        var s15RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step15", Page, "Sort by Effective Date",
            action: async () => await ClickSortByText("Effective Date"),
            assertions: async () =>
            {
                var catInd = await GetSortIndicator("Category");
                var dateInd = await GetSortIndicator("Effective Date");
                var seqList = await GetColumnByIndex(4);
                var seq = string.Join(",", seqList);
                var rowCount = (await GetRowCount()).ToString();
                runner.Capture("S_sortDate", "sort.indicator[Category]", catInd);
                runner.Capture("S_sortDate", "sort.indicator[Effective Date]", dateInd);
                runner.Capture("S_sortDate", "grid.column[Effective Date].sequence", seq);
                runner.AssertEquals("Step15", "sort.indicator[Category]", catInd, "none");
                runner.AssertEquals("Step15", "sort.indicator[Effective Date]", dateInd, "asc");
                runner.Assert("Step15", "grid.column[Effective Date].sequence", IsOrdered(seqList, CmpDate),
                    $"Effective Dates oldest to newest: {seq}", $"Effective Dates NOT ascending: {seq}");
                runner.AssertUnchanged("Step15", "grid.rowCount", s15RowsBefore, rowCount);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 16 — Sort Status
        // ════════════════════════════════════════════════════════════════
        var s16RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step16", Page, "Sort by Status",
            action: async () => await ClickSortByText("Status"),
            assertions: async () =>
            {
                var dateInd = await GetSortIndicator("Effective Date");
                var statusInd = await GetSortIndicator("Status");
                var seqList = await GetColumnByIndex(5);
                var seq = string.Join(",", seqList);
                var rowCount = (await GetRowCount()).ToString();
                runner.Capture("S_sortStatus", "sort.indicator[Effective Date]", dateInd);
                runner.Capture("S_sortStatus", "sort.indicator[Status]", statusInd);
                runner.Capture("S_sortStatus", "grid.column[Status].sequence", seq);
                runner.AssertEquals("Step16", "sort.indicator[Effective Date]", dateInd, "none");
                runner.AssertEquals("Step16", "sort.indicator[Status]", statusInd, "asc");
                runner.Assert("Step16", "grid.column[Status].sequence", IsOrdered(seqList, CmpStatus),
                    $"Rows grouped by status ascending (Inactive first): {seq}", $"Status NOT in ascending order: {seq}");
                runner.AssertUnchanged("Step16", "grid.rowCount", s16RowsBefore, rowCount);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 17 — Sort ID (back to the default order)
        // ════════════════════════════════════════════════════════════════
        await runner.ExecuteStep("Step17", Page, "Sort by ID (back to the default order)",
            action: async () => await ClickSortByText("ID"),
            assertions: async () =>
            {
                var statusInd = await GetSortIndicator("Status");
                var idInd = await GetSortIndicator("ID");
                var idSeq = string.Join(",", await GetRowIds());
                var pager = await GetPagerInfo();
                runner.Capture("S_sortId", "sort.indicator[Status]", statusInd);
                runner.Capture("S_sortId", "sort.indicator[ID]", idInd);
                runner.Capture("S_sortId", "grid.column[ID].sequence", idSeq);
                runner.Capture("S_sortId", "pager.info", pager);
                runner.AssertEquals("Step17", "sort.indicator[Status]", statusInd, "none");
                runner.AssertEquals("Step17", "sort.indicator[ID]", idInd, "asc");
                runner.AssertEquals("Step17", "grid.column[ID].sequence", idSeq, "1,2,3,4,5,6,7,8,9,10");
                runner.AssertUnchanged("Step17", "grid.column[ID].sequence", baseRowIds, idSeq);
                runner.AssertEquals("Step17", "pager.info", pager, RefPage1Pager);
            },
            gridSelector: "table.mud-table-root tbody");

        runner.Log("══════════════════════════════════════════════");
        runner.Log("TARGET MODULE A COMPLETE");
        runner.Log("══════════════════════════════════════════════");

        await runner.GenerateReport();

        if (runner.StepsFailed > 0 || runner.AssertionsFailed > 0)
            MarkIssueFound($"Module A target: {runner.StepsFailed} step(s), {runner.AssertionsFailed} assertion(s) failed vs reference baseline");
        else
            MarkVerified();
    }
}

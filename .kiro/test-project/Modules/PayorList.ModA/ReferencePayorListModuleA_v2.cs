// plan-revision: 1; steps=17
using Microsoft.Playwright;
using NUnit.Framework;
using MigrationTests.Playwright;
using MigrationTests.Playwright.Helpers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MigrationTests.Playwright.Tests;

/// <summary>
/// REFERENCE MODULE A — PayorList: Load the page and explore the grid (sort &amp; pagination).
///
/// Workflow (from transitions.md): "I open Payor Information. I want to see all payors, flip
/// through the pages, resize the page, jump to a page, and sort columns to find what I need —
/// without anything else on the page changing."
///
/// Reference system = ASP.NET WebForms + free-jqGrid at /Pages/PayorList.aspx (port 5100).
/// Dataset = 15 fixed records (IDs 1–15), default pageSize 10 → 2 pages.
///
/// This captures the GROUND TRUTH baseline. Every value asserted/captured here becomes the
/// expected value for the Target (Blazor) comparison in Phase 2.
///
/// 17 steps (Step1..Step17) per state-map/plan.json Module A + assertions.md Module A.
///
/// Run: APP_TEST_MODE=validate dotnet test --filter "Name~ReferencePayorList_ModA" --no-build
/// </summary>
[TestFixture]
public class ReferencePayorListModuleA_v2 : TestBase
{
    // ─────────────────────────────────────────────────────────────────────
    // jqGrid selectors (free-jqGrid 4.15.5 conventions — see PayorList.aspx)
    // Grid table id = payorGrid, pager div id = payorPager.
    // ─────────────────────────────────────────────────────────────────────
    private const string GridRowsSel = "#payorGrid tr.jqgrow";                 // data rows (id = record id)
    private const string PagerInfoSel = "#payorPager .ui-paging-info";          // "View 1 - 10 of 15"
    private const string NextSel = "#next_payorPager";                          // next-page nav icon
    private const string PrevSel = "#prev_payorPager";                          // prev-page nav icon
    private const string FirstSel = "#first_payorPager";                        // first-page nav icon
    private const string LastSel = "#last_payorPager";                          // last-page nav icon
    private const string PageInputSel = "#payorPager input.ui-pg-input";        // direct page-number box
    private const string PageSizeSel = "#payorPager select.ui-pg-selbox";       // rows-per-page select
    private const string NameHeaderSel = "#jqgh_payorGrid_name";                // sortable header: Payor Name
    private const string CodeHeaderSel = "#jqgh_payorGrid_code";                // sortable header: Code
    private const string CategoryHeaderSel = "#jqgh_payorGrid_category";        // sortable header: Category
    private const string DateHeaderSel = "#jqgh_payorGrid_effectiveDate";       // sortable header: Effective Date
    private const string StatusHeaderSel = "#jqgh_payorGrid_isActive";          // sortable header: Status
    private const string IdHeaderSel = "#jqgh_payorGrid_id";                    // sortable header: ID
    private const string SaveBtnSel = "#btnSaveAll";
    private const string CancelBtnSel = "#btnCancelAll";

    #region Navigation

    private async Task<bool> NavigateToPage()
    {
        await Page.GotoAsync($"{Settings.ReferenceBaseUrl}/Pages/PayorList.aspx");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AssertNotRedirectedToAuth();

        // Wait for jqGrid to render its first data rows (AJAX load from PayorService.asmx)
        try
        {
            await Page.WaitForSelectorAsync(GridRowsSel,
                new() { Timeout = 20000, State = WaitForSelectorState.Visible });
            await Page.WaitForTimeoutAsync(1500); // let gridComplete settle
        }
        catch (TimeoutException)
        {
            await CaptureDiagnosticHtmlAsync(GridRowsSel, "grid rows never appeared");
            return false;
        }

        return Page.Url.Contains("PayorList", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Grid read helpers

    private async Task<int> GetRowCount() => await Page.Locator(GridRowsSel).CountAsync();

    /// <summary>Row DOM id == record id. Returns ids in display order (e.g. "1","2",...).</summary>
    private async Task<List<string>> GetRowIds()
    {
        var ids = new List<string>();
        var rows = Page.Locator(GridRowsSel);
        int n = await rows.CountAsync();
        for (int i = 0; i < n; i++)
        {
            var id = await rows.Nth(i).GetAttributeAsync("id");
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Reads a text column (name/code) for the first N rows, in display order.</summary>
    private async Task<List<string>> GetColumnSequence(string cellClass, int max = 10)
    {
        var vals = new List<string>();
        var cells = Page.Locator($"{GridRowsSel} span.{cellClass}");
        int n = await cells.CountAsync();
        for (int i = 0; i < Math.Min(n, max); i++)
        {
            var t = (await cells.Nth(i).TextContentAsync())?.Trim() ?? "";
            vals.Add(t);
        }
        return vals;
    }

    private async Task<string> GetFirstRowId()
    {
        var ids = await GetRowIds();
        return ids.FirstOrDefault() ?? "";
    }

    private async Task<string> GetFirstRowName()
    {
        var seq = await GetColumnSequence("cell-name", 1);
        return seq.FirstOrDefault() ?? "";
    }

    private async Task<string> GetFirstRowCode()
    {
        var seq = await GetColumnSequence("cell-code", 1);
        return seq.FirstOrDefault() ?? "";
    }

    private async Task<string> GetPagerInfo() =>
        (await Page.Locator(PagerInfoSel).First.TextContentAsync())?.Trim() ?? "";

    private async Task<bool> IsButtonEnabled(string selector)
    {
        var el = Page.Locator(selector).First;
        if (await el.CountAsync() == 0) return false;
        return await el.IsEnabledAsync();
    }

    /// <summary>
    /// jqGrid column header map, "index:label" joined by '|'.
    /// Header labels live in div[id^='jqgh_payorGrid_'] whose first text node is the label
    /// (followed by a .s-ico sort-glyph span). We read the clean label text per column in
    /// header order via the column's own id suffix, so icon markup never leaks into the value.
    /// </summary>
    private async Task<string> GetColumnMap()
    {
        var labels = await Page.EvaluateAsync<string[]>(@"() => {
            const ths = Array.from(document.querySelectorAll('#gview_payorGrid table.ui-jqgrid-htable thead tr.ui-jqgrid-labels th'));
            return ths.map(th => {
                const div = th.querySelector(""div[id^='jqgh_']"");
                if (!div) return '';
                // First child text node is the header label (before the .s-ico span).
                let label = '';
                for (const node of div.childNodes) {
                    if (node.nodeType === Node.TEXT_NODE) label += node.textContent;
                }
                return label.trim();
            });
        }");

        var parts = new List<string>();
        for (int i = 0; i < labels.Length; i++)
            parts.Add($"{i}:{labels[i]}");
        return string.Join("|", parts);
    }

    /// <summary>
    /// jqGrid sort indicator state for a column: "none" | "asc" | "desc".
    /// The active glyph span lacks the "ui-state-disabled" class.
    /// </summary>
    private async Task<string> GetSortIndicator(string colName)
    {
        var asc = Page.Locator($"#jqgh_payorGrid_{colName} span.s-ico span.ui-icon-asc").First;
        var desc = Page.Locator($"#jqgh_payorGrid_{colName} span.s-ico span.ui-icon-desc").First;
        bool ascActive = await asc.CountAsync() > 0 &&
                         !(await asc.GetAttributeAsync("class") ?? "").Contains("ui-state-disabled");
        bool descActive = await desc.CountAsync() > 0 &&
                          !(await desc.GetAttributeAsync("class") ?? "").Contains("ui-state-disabled");
        if (ascActive) return "asc";
        if (descActive) return "desc";
        return "none";
    }

    /// <summary>Total record count "Z" from the pager text "View X - Y of Z".</summary>
    private static string ParseTotal(string pager)
    {
        var m = Regex.Match(pager, @"of\s+([\d,]+)");
        return m.Success ? m.Groups[1].Value.Replace(",", "") : "";
    }

    private async Task<string> GetPagerTotal() => ParseTotal(await GetPagerInfo());

    private async Task<string> GetPageSize() =>
        await Page.Locator(PageSizeSel).First.InputValueAsync();

    private async Task<string> GetCurrentPage() =>
        await Page.Locator(PageInputSel).First.InputValueAsync();

    private async Task<string> GetChangeCount() =>
        (await Page.Locator("#changeCount").First.TextContentAsync())?.Trim() ?? "";

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
        await Page.WaitForTimeoutAsync(3000); // per pagination-sorting-standards
    }

    private async Task SetPageSize(string value)
    {
        await Page.Locator(PageSizeSel).First.SelectOptionAsync(value);
        await Page.WaitForTimeoutAsync(3000);
    }

    private async Task SetDirectPage(int page)
    {
        var box = Page.Locator(PageInputSel).First;
        await box.FillAsync(page.ToString());
        await box.PressAsync("Enter");
        await Page.WaitForTimeoutAsync(3000);
    }

    private async Task ClickSort(string headerSel)
    {
        await Page.Locator(headerSel).First.ClickAsync();
        await Page.WaitForTimeoutAsync(3000);
    }

    private async Task ResetToPage1()
    {
        // Ensure first page before sort steps (sort should reset to page 1 anyway).
        var firstEnabled = await Page.Locator($"{FirstSel}").First.CountAsync() > 0;
        if (firstEnabled)
        {
            try { await ClickPagerNav(FirstSel); } catch { }
        }
    }

    #endregion

    [Test]
    public async Task ReferencePayorList_ModA()
    {
        var runner = new ModuleTestRunner("PayorList", "A", "Reference", 1);

        runner.Log("══════════════════════════════════════════════");
        runner.Log("REFERENCE MODULE A — PayorList: Load & Explore Grid");
        runner.Log("══════════════════════════════════════════════");

        if (!await NavigateToPage())
        {
            runner.StepFail("NAV", "Navigation to reference PayorList failed (grid rows never rendered)");
            await runner.GenerateReport();
            MarkIssueFound("Navigation failed — reference grid did not load");
            return;
        }
        runner.StepPass("NAV", "Opened reference PayorList.aspx (jqGrid) and data rows rendered.");

        // ════════════════════════════════════════════════════════════════
        // STEP 1 — Page load (reach S0): verify grid, pager, first row, counter, buttons disabled
        // ════════════════════════════════════════════════════════════════
        await runner.ExecuteStep("Step1", Page, "Load the page and verify the initial grid (S0)",
            action: async () => { await Task.CompletedTask; },
            assertions: async () =>
            {
                var heading = (await Page.Locator("h1").First.TextContentAsync())?.Trim() ?? "";
                runner.Capture("S0", "heading.text", heading);
                runner.AssertEquals("Step1", "heading.text", heading, "Payor Information Management");

                var gridVisible = await Page.Locator("#payorGrid").First.IsVisibleAsync();
                runner.Assert("Step1", "grid.visible", gridVisible, "Grid is visible", "Grid NOT visible");

                var rowCount = await GetRowCount();
                runner.Capture("S0", "grid.rowCount", rowCount.ToString());
                runner.AssertEquals("Step1", "grid.rowCount", rowCount.ToString(), "10");

                var pager = await GetPagerInfo();
                runner.Capture("S0", "pager.info", pager);
                runner.AssertEquals("Step1", "pager.info", pager, "View 1 - 10 of 15");

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

                var saveEnabled = (await IsButtonEnabled(SaveBtnSel)).ToString().ToLower();
                var cancelEnabled = (await IsButtonEnabled(CancelBtnSel)).ToString().ToLower();
                runner.Capture("S0", "saveButton.enabled", saveEnabled);
                runner.Capture("S0", "cancelButton.enabled", cancelEnabled);
                runner.AssertEquals("Step1", "saveButton.enabled", saveEnabled, "false");
                runner.AssertEquals("Step1", "cancelButton.enabled", cancelEnabled, "false");
            },
            gridSelector: "#payorGrid tbody");

        await runner.SaveSnapshot(Page, "S0", "page_load", "#gview_payorGrid");

        // CSS capture — page_load (S0), after Step1 per css-capture-plan.md
        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "reference", "page_load"); } catch { }

        // Step1 baseline values (runtime) used as "before" / "equals Step1" values below.
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
                runner.AssertEquals("Step2", "grid.columnMap", map,
                    "0:ID|1:Payor Name|2:Code|3:Category|4:Effective Date|5:Status");

                var rowCount = await GetRowCount();
                runner.AssertUnchanged("Step2", "grid.rowCount", s2RowsBefore, rowCount.ToString());
                var pager = await GetPagerInfo();
                runner.AssertUnchanged("Step2", "pager.info", s2PagerBefore, pager);
            },
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 3 — Next page (1 → 2)
        // ════════════════════════════════════════════════════════════════
        var s3PagerBefore = await GetPagerInfo();
        var s3IdBefore = await GetFirstRowId();
        var s3RowIdsBefore = string.Join(",", await GetRowIds());
        var s3SaveBefore = (await IsButtonEnabled(SaveBtnSel)).ToString().ToLower();
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
                runner.AssertEquals("Step3", "pager.info", pager, "View 11 - 15 of 15");
                runner.AssertChanged("Step3", "grid.row[0].id", s3IdBefore, firstId);
                runner.AssertEquals("Step3", "grid.row[0].id", firstId, "11");
                runner.AssertChanged("Step3", "grid.rowIds", s3RowIdsBefore, rowIds);
                runner.AssertEquals("Step3", "grid.rowCount", rowCount.ToString(), "5");
                runner.AssertUnchanged("Step3", "pager.total", baseTotal, ParseTotal(pager));
                runner.AssertUnchanged("Step3", "saveButton.enabled",
                    s3SaveBefore, (await IsButtonEnabled(SaveBtnSel)).ToString().ToLower());
            },
            gridSelector: "#payorGrid tbody");

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
                runner.AssertEquals("Step4", "pager.info", pager, "View 1 - 10 of 15");
                runner.AssertChanged("Step4", "grid.row[0].id", s4IdBefore, firstId);
                runner.AssertEquals("Step4", "grid.row[0].id", firstId, "1");
                runner.AssertEquals("Step4", "grid.rowCount", rowCount.ToString(), "10");
                runner.AssertUnchanged("Step4", "pager.total", baseTotal, ParseTotal(pager));
            },
            gridSelector: "#payorGrid tbody");

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
                runner.AssertEquals("Step5", "pager.info", pager, "View 11 - 15 of 15");
                runner.AssertEquals("Step5", "grid.row[0].id", firstId, "11");
                runner.AssertEquals("Step5", "grid.rowCount", rowCount.ToString(), "5");
                runner.AssertUnchanged("Step5", "pager.total", baseTotal, ParseTotal(pager));
            },
            gridSelector: "#payorGrid tbody");

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
                runner.AssertEquals("Step6", "pager.info", pager, "View 1 - 10 of 15");
                runner.AssertEquals("Step6", "grid.row[0].id", firstId, "1");
                runner.AssertUnchanged("Step6", "pager.total", baseTotal, ParseTotal(pager));
            },
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 7 — Page size 10 → 15
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
                runner.AssertEquals("Step7", "pager.info", pager, "View 1 - 15 of 15");
                runner.AssertUnchanged("Step7", "pager.total", baseTotal, ParseTotal(pager));
                runner.AssertUnchanged("Step7", "grid.row[0].id", s7IdBefore, firstId);
            },
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 8 — Page size 15 → 10 (restore)
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
                runner.AssertEquals("Step8", "pager.info", pager, "View 1 - 10 of 15");
                runner.AssertUnchanged("Step8", "pager.total", baseTotal, ParseTotal(pager));
            },
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 9 — Direct page input → page 2
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
                runner.AssertEquals("Step9", "pager.info", pager, "View 11 - 15 of 15");
                runner.AssertEquals("Step9", "grid.row[0].id", firstId, "11");
                runner.AssertUnchanged("Step9", "pager.total", baseTotal, ParseTotal(pager));
                runner.AssertUnchanged("Step9", "pager.pageSize", s9SizeBefore, await GetPageSize());
            },
            gridSelector: "#payorGrid tbody");

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
                runner.AssertEquals("Step10", "pager.info", pager, "View 1 - 10 of 15");
                runner.AssertChanged("Step10", "grid.row[0].id", s10IdBefore, firstId);
                runner.AssertEquals("Step10", "grid.row[0].id", firstId, "1");
                runner.AssertUnchanged("Step10", "pager.total", baseTotal, ParseTotal(pager));
                runner.AssertUnchanged("Step10", "pager.pageSize", s10SizeBefore, await GetPageSize());
            },
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 11 — Sort Payor Name ASC (S3)
        // ════════════════════════════════════════════════════════════════
        var nameSeqBefore = await GetSeq(1);
        var s11RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step11", Page, "Sort by Payor Name ascending (click header once)",
            action: async () => await ClickSort(NameHeaderSel),
            assertions: async () =>
            {
                var ind = await GetSortIndicator("name");
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
            gridSelector: "#payorGrid tbody");

        // CSS capture — sorted (after Step11, Payor Name ascending)
        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "reference", "sorted"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 12 — Sort Payor Name DESC (toggle)
        // ════════════════════════════════════════════════════════════════
        var s12NameBefore = await GetFirstRowName();
        var s12RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step12", Page, "Sort by Payor Name descending (click header again)",
            action: async () => await ClickSort(NameHeaderSel),
            assertions: async () =>
            {
                var ind = await GetSortIndicator("name");
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 13 — Sort Code (switch sort column)
        // ════════════════════════════════════════════════════════════════
        var s13RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step13", Page, "Sort by Code (switch sort column)",
            action: async () => await ClickSort(CodeHeaderSel),
            assertions: async () =>
            {
                var nameInd = await GetSortIndicator("name");
                var codeInd = await GetSortIndicator("code");
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 14 — Sort Category
        // ════════════════════════════════════════════════════════════════
        var s14RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step14", Page, "Sort by Category",
            action: async () => await ClickSort(CategoryHeaderSel),
            assertions: async () =>
            {
                var codeInd = await GetSortIndicator("code");
                var catInd = await GetSortIndicator("category");
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 15 — Sort Effective Date
        // ════════════════════════════════════════════════════════════════
        var s15RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step15", Page, "Sort by Effective Date",
            action: async () => await ClickSort(DateHeaderSel),
            assertions: async () =>
            {
                var catInd = await GetSortIndicator("category");
                var dateInd = await GetSortIndicator("effectiveDate");
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 16 — Sort Status
        // ════════════════════════════════════════════════════════════════
        var s16RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step16", Page, "Sort by Status",
            action: async () => await ClickSort(StatusHeaderSel),
            assertions: async () =>
            {
                var dateInd = await GetSortIndicator("effectiveDate");
                var statusInd = await GetSortIndicator("isActive");
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 17 — Sort ID (back to the default order)
        // ════════════════════════════════════════════════════════════════
        await runner.ExecuteStep("Step17", Page, "Sort by ID (back to the default order)",
            action: async () => await ClickSort(IdHeaderSel),
            assertions: async () =>
            {
                var statusInd = await GetSortIndicator("isActive");
                var idInd = await GetSortIndicator("id");
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
                runner.AssertEquals("Step17", "pager.info", pager, "View 1 - 10 of 15");
            },
            gridSelector: "#payorGrid tbody");

        runner.Log("══════════════════════════════════════════════");
        runner.Log("REFERENCE MODULE A COMPLETE");
        runner.Log("══════════════════════════════════════════════");

        await runner.GenerateReport();

        if (runner.StepsFailed > 0 || runner.AssertionsFailed > 0)
            MarkIssueFound($"Module A reference: {runner.StepsFailed} step(s), {runner.AssertionsFailed} assertion(s) failed");
        else
            MarkVerified();
    }
}

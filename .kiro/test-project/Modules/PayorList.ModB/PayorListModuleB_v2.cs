// plan-revision: 1; steps=12
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;
using MigrationTests.Playwright;
using MigrationTests.Playwright.Helpers;

namespace MigrationTests.Playwright.Tests;

/// <summary>
/// TARGET MODULE B — PayorList: Search with Filters (Name / Code / Status), no-match search,
/// and search from page 2. READ-ONLY.
///
/// Same human workflow as ReferencePayorListModuleB_v2, run against the TARGET system:
///   Blazor Server + MudBlazor MudDataGrid at /Pages/PayorList.
///
/// Target DOM (PayorList.razor): filter panel = MudTextField (placeholder "Search by name..." /
/// "Search by code..."), MudSelect "Status:" (All / Active / Inactive, ToStringFunc shows the
/// label), MudButtons Search / Clear. MudDataGridPager shows "View {first} - {last} of {all}".
/// The pager has no page-number box: pager.page is derived from the caption start and the
/// rows-per-page value (page = (start - 1) / size + 1).
///
/// grid.tbody.structure uses the Reference vocabulary so the "no placeholder row in an empty
/// grid" check is comparable: the jqGrid hidden sizing row ("jqgfirstrow") has no Target
/// counterpart and is always listed first, Target data rows map to "jqgrow", and any other
/// Target row is reported by its class list (e.g. an empty-records row) and so shows up as a
/// difference.
///
/// Expected EQUALS constants are the Reference constants verbatim; CHANGES / STAYS SAME
/// before-values are read from the Target at runtime. Actions catch their own errors (logged).
///
/// 12 steps per plan.json Module B: Step1..Step7, B1_Step1..B1_Step2, B2_Step1..B2_Step3.
/// CSS states: filtered (after Step2), no_results (after B1_Step1).
/// </summary>
[TestFixture]
public class PayorListModuleB_v2 : TestBase
{
    // Data rows only (a MudDataGrid empty-records row has a single spanning cell).
    private const string GridRowsSel = "table.mud-table-root tbody tr.mud-table-row:has(td:nth-child(2))";
    private const string PagerRangeSel = ".mud-table-pagination-caption";
    private const string NextSel = "button[aria-label='Next Page']";
    private const string NameFilterSel = "input[placeholder='Search by name...']";
    private const string CodeFilterSel = "input[placeholder='Search by code...']";
    private const string SearchBtnSel = "#btnSearch";
    private const string ClearBtnSel = "#btnClear";
    private const string SaveBtnSel = "#btnSaveAll";
    private const string CancelBtnSel = "#btnCancelAll";
    private const string ErrorToastSel = ".mud-snackbar[class*='error']";

    private const string NameFilterValue = "Mesa";
    private const string CodeFilterValue = "BBHS";
    private const string StatusInactiveText = "Inactive";
    private const string StatusActiveText = "Active";
    private const string NoMatchValue = "ZZZNoMatch";

    // Reference baseline constants (copied verbatim from ReferencePayorListModuleB_v2)
    private const string FullPager = "View 1 - 10 of 15";
    private const string FullTotal = "15";
    private const string FullPage1Ids = "1,2,3,4,5,6,7,8,9,10";
    private const string Page2Pager = "View 11 - 15 of 15";
    private const string Page2FirstId = "11";
    private const string ActivePager = "View 1 - 10 of 13";
    private const string ActiveTotal = "13";
    private const string ActivePage1Ids = "1,2,3,4,5,6,7,9,10,12";

    #region Navigation

    private async Task<bool> NavigateToPage()
    {
        await Page.GotoAsync($"{Settings.BaseUrl}/Pages/PayorList");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AssertNotRedirectedToAuth();
        try
        {
            await Page.WaitForSelectorAsync(GridRowsSel,
                new() { Timeout = 20000, State = WaitForSelectorState.Visible });
            await Page.WaitForTimeoutAsync(1500); // Blazor Server hydration
        }
        catch (TimeoutException)
        {
            await CaptureDiagnosticHtmlAsync(GridRowsSel, "MudDataGrid rows never appeared");
            return false;
        }
        return Page.Url.Contains("PayorList", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Read helpers

    private async Task<int> GetRowCount() => await Page.Locator(GridRowsSel).CountAsync();

    private async Task<List<string>> GetColumnByIndex(int col)
    {
        var vals = new List<string>();
        var rows = Page.Locator(GridRowsSel);
        int n = await rows.CountAsync();
        for (int i = 0; i < n; i++)
            vals.Add((await rows.Nth(i).Locator("td").Nth(col).TextContentAsync())?.Trim() ?? "");
        return vals;
    }

    private async Task<string> GetRowIds() => string.Join(",", (await GetColumnByIndex(0)).Where(s => s != ""));
    private async Task<List<string>> GetNames() => await GetColumnByIndex(1);
    private async Task<List<string>> GetCodes() => await GetColumnByIndex(2);
    private async Task<List<string>> GetStatusColumn() => await GetColumnByIndex(5);

    private async Task<string> GetPagerInfo()
    {
        var caps = Page.Locator(PagerRangeSel);
        int n = await caps.CountAsync();
        if (n == 0) return "";
        return (await caps.Nth(n - 1).TextContentAsync())?.Trim() ?? "";
    }

    /// <summary>Total record count from the pager text ("View x - y of z" → z); "" when no range is shown.</summary>
    private static string TotalFrom(string pager)
    {
        var m = Regex.Match(pager, @"of\s+([\d,]+)\s*$");
        return m.Success ? m.Groups[1].Value.Replace(",", "") : "";
    }

    private async Task<string> GetCurrentPage()
    {
        var m = Regex.Match(await GetPagerInfo(), @"(\d+)\s*-\s*\d+");
        var sizeInput = Page.Locator(".mud-table-pagination input");
        var sizeText = await sizeInput.CountAsync() > 0 ? await sizeInput.First.InputValueAsync() : "";
        if (!m.Success || !int.TryParse(sizeText, out var size) || size <= 0) return "";
        var start = int.Parse(m.Groups[1].Value);
        return start <= 0 ? "" : (((start - 1) / size) + 1).ToString();
    }

    private async Task<string> GetValue(string sel)
    {
        var el = Page.Locator(sel);
        return await el.CountAsync() > 0 ? await el.First.InputValueAsync() : "";
    }

    private ILocator StatusSelect() =>
        Page.Locator(".mud-select").Filter(new() { Has = Page.Locator("label", new() { HasTextString = "Status" }) }).First;

    /// <summary>filter.status.value = the label the Status MudSelect shows (All / Active / Inactive).</summary>
    private async Task<string> GetStatusFilterLabel()
    {
        var input = StatusSelect().Locator("input");
        return await input.CountAsync() > 0 ? (await input.First.InputValueAsync()).Trim() : "";
    }

    private async Task<string> IsEnabled(string sel)
    {
        var el = Page.Locator(sel).First;
        if (await el.CountAsync() == 0) return "false";
        return (await el.IsEnabledAsync()).ToString().ToLower();
    }

    private async Task<string> GetTbodyStructure() =>
        await Page.EvaluateAsync<string>(@"() => {
            const trs = Array.from(document.querySelectorAll('table.mud-table-root tbody > tr'));
            const parts = ['jqgfirstrow'];
            trs.forEach(tr => parts.push(tr.querySelectorAll('td').length >= 2 ? 'jqgrow' : (tr.className || 'tr')));
            return parts.join(',');
        }");

    private async Task<string> IsErrorVisible()
    {
        var el = Page.Locator(ErrorToastSel);
        return (await el.CountAsync() > 0 && await el.First.IsVisibleAsync()).ToString().ToLower();
    }

    /// <summary>Runtime snapshot of the fields Module B compares before/after a step.</summary>
    private sealed record Snap(string Rows, string Pager, string Total, string Ids, string Row0Id,
        string Name, string Code, string Status, string Save);

    private async Task<Snap> ReadSnap()
    {
        var pager = await GetPagerInfo();
        var ids = await GetRowIds();
        return new Snap(
            (await GetRowCount()).ToString(), pager, TotalFrom(pager), ids,
            ids.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "",
            await GetValue(NameFilterSel), await GetValue(CodeFilterSel),
            await GetStatusFilterLabel(), await IsEnabled(SaveBtnSel));
    }

    #endregion

    #region Action helpers

    private async Task Try(ModuleTestRunner runner, string step, Func<Task> act)
    {
        try { await act(); }
        catch (Exception e) { runner.Log($"[{step}] action error: {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
    }

    private async Task ClickAndWait(string sel)
    {
        var el = Page.Locator(sel).First;
        await el.ScrollIntoViewIfNeededAsync(new() { Timeout = 5000 });
        await el.ClickAsync(new() { Timeout = 5000 });
        await Page.WaitForTimeoutAsync(2000); // Blazor re-render settle
    }

    private async Task FillFilter(string sel, string value)
    {
        await Page.Locator(sel).First.FillAsync(value, new() { Timeout = 5000 });
        await Page.WaitForTimeoutAsync(300);
    }

    private async Task PressEnterAndWait(string sel)
    {
        await Page.Locator(sel).First.PressAsync("Enter", new() { Timeout = 5000 });
        await Page.WaitForTimeoutAsync(2000);
    }

    private async Task SelectStatus(string label)
    {
        // The visible select slot opens the popover (a click on the outer .mud-select does not).
        await StatusSelect().Locator("div.mud-input-slot").First.ClickAsync(new() { Timeout = 5000 });
        await Page.WaitForTimeoutAsync(600);
        await Page.Locator(".mud-popover-open .mud-list-item")
            .Filter(new() { HasTextRegex = new Regex($"^\\s*{label}\\s*$") })
            .First.ClickAsync(new() { Timeout = 5000 });
        await Page.WaitForTimeoutAsync(600);
    }

    #endregion

    /// <summary>Captures and asserts the restored, unfiltered first page (shared by every Clear step).</summary>
    private async Task AssertFullList(ModuleTestRunner runner, string step, string stateId, Snap before)
    {
        var now = await ReadSnap();
        var page = await GetCurrentPage();

        runner.Capture(stateId, "grid.rowCount", now.Rows);
        runner.Capture(stateId, "pager.info", now.Pager);
        runner.Capture(stateId, "pager.total", now.Total);
        runner.Capture(stateId, "grid.rowIds", now.Ids);
        runner.Capture(stateId, "filters", $"name='{now.Name}', code='{now.Code}', status='{now.Status}'");

        runner.AssertEquals(step, "pager.info", now.Pager, FullPager);
        runner.AssertEquals(step, "pager.total", now.Total, FullTotal);
        runner.AssertEquals(step, "grid.rowIds", now.Ids, FullPage1Ids);
        runner.AssertEquals(step, "grid.rowCount", now.Rows, "10");
        runner.AssertEquals(step, "filter.name.value", now.Name, "");
        runner.AssertEquals(step, "filter.code.value", now.Code, "");
        runner.AssertEquals(step, "filter.status.value", now.Status, "All");
        runner.AssertEquals(step, "pager.page", page, "1");
        runner.AssertUnchanged(step, "saveButton.enabled", before.Save, now.Save);
    }

    [Test]
    public async Task PayorList_ModB()
    {
        var runner = new ModuleTestRunner("PayorList", "B", "Target", 1);

        runner.Log("══════════════════════════════════════════════");
        runner.Log("TARGET MODULE B — PayorList: Search with Filters (Blazor/MudDataGrid)");
        runner.Log("══════════════════════════════════════════════");

        if (!await NavigateToPage())
        {
            runner.StepFail("NAV", "Navigation to target PayorList failed (grid rows never rendered)");
            await runner.GenerateReport();
            MarkIssueFound("Navigation failed — target grid did not load");
            return;
        }

        // ════════════════════════════════════════════════════════════════
        // STEP 1 — Unfiltered starting state (S0)
        // ════════════════════════════════════════════════════════════════
        await runner.ExecuteStep("Step1", Page, "Verify the unfiltered starting state (S0)",
            action: async () => { await Task.CompletedTask; },
            assertions: async () =>
            {
                var s = await ReadSnap();
                runner.Capture("S0", "grid.rowCount", s.Rows);
                runner.Capture("S0", "pager.info", s.Pager);
                runner.Capture("S0", "pager.total", s.Total);
                runner.Capture("S0", "grid.rowIds", s.Ids);

                runner.AssertEquals("Step1", "filter.name.value", s.Name, "");
                runner.AssertEquals("Step1", "filter.code.value", s.Code, "");
                runner.AssertEquals("Step1", "filter.status.value", s.Status, "All");
                runner.AssertEquals("Step1", "pager.info", s.Pager, FullPager);
                runner.AssertEquals("Step1", "pager.total", s.Total, FullTotal);
                runner.AssertEquals("Step1", "grid.rowIds", s.Ids, FullPage1Ids);
                runner.AssertEquals("Step1", "grid.rowCount", s.Rows, "10");
                runner.AssertEquals("Step1", "saveButton.enabled", s.Save, "false");
                runner.AssertEquals("Step1", "cancelButton.enabled", await IsEnabled(CancelBtnSel), "false");
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 2 — Name filter "Mesa" + Search (S4)
        // ════════════════════════════════════════════════════════════════
        var b2 = await ReadSnap();
        await runner.ExecuteStep("Step2", Page, $"Type '{NameFilterValue}' in Payor Name and click Search",
            action: async () => await Try(runner, "Step2", async () =>
            {
                await FillFilter(NameFilterSel, NameFilterValue);
                await ClickAndWait(SearchBtnSel);
            }),
            assertions: async () =>
            {
                var s = await ReadSnap();
                var names = await GetNames();
                runner.Capture("S4_name", "grid.rowCount", s.Rows);
                runner.Capture("S4_name", "pager.info", s.Pager);
                runner.Capture("S4_name", "pager.total", s.Total);
                runner.Capture("S4_name", "grid.rowIds", s.Ids);
                runner.Capture("S4_name", "grid.column[Payor Name].sequence", string.Join(",", names));

                runner.AssertChanged("Step2", "filter.name.value", b2.Name, s.Name);
                runner.AssertEquals("Step2", "filter.name.value", s.Name, NameFilterValue);
                runner.Assert("Step2", "grid.column[Payor Name].allMatch",
                    names.Count > 0 && names.All(n => n.Contains(NameFilterValue, StringComparison.OrdinalIgnoreCase)),
                    $"All {names.Count} Payor Names contain '{NameFilterValue}': {string.Join(", ", names)}",
                    $"Not every Payor Name contains '{NameFilterValue}' ({names.Count} rows): {string.Join(", ", names)}");
                runner.AssertEquals("Step2", "grid.column[Payor Name].sequence", string.Join(",", names), "Mesa Senior Plan A,Mesa Senior Plan B");
                runner.AssertChanged("Step2", "pager.total", b2.Total, s.Total);
                runner.AssertEquals("Step2", "pager.total", s.Total, "2");
                runner.AssertChanged("Step2", "pager.info", b2.Pager, s.Pager);
                runner.AssertEquals("Step2", "pager.info", s.Pager, "View 1 - 2 of 2");
                runner.AssertEquals("Step2", "grid.rowCount", s.Rows, "2");
                runner.AssertChanged("Step2", "grid.rowIds", b2.Ids, s.Ids);
                runner.AssertEquals("Step2", "grid.rowIds", s.Ids, "13,14");
                runner.AssertUnchanged("Step2", "filter.code.value", b2.Code, s.Code);
                runner.AssertUnchanged("Step2", "filter.status.value", b2.Status, s.Status);
                runner.AssertUnchanged("Step2", "saveButton.enabled", b2.Save, s.Save);
            },
            gridSelector: "table.mud-table-root tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "target", "filtered"); } catch { }
        await runner.SaveSnapshot(Page, "S4_name", "filtered_name", "table.mud-table-root");

        // ════════════════════════════════════════════════════════════════
        // STEP 3 — Clear (after Name filter)
        // ════════════════════════════════════════════════════════════════
        var b3 = await ReadSnap();
        await runner.ExecuteStep("Step3", Page, "Click Clear (after the Name filter)",
            action: async () => await Try(runner, "Step3", () => ClickAndWait(ClearBtnSel)),
            assertions: async () =>
            {
                var s = await ReadSnap();
                runner.AssertChanged("Step3", "filter.name.value", b3.Name, s.Name);
                runner.AssertChanged("Step3", "grid.rowIds", b3.Ids, s.Ids);
                runner.AssertChanged("Step3", "pager.total", b3.Total, s.Total);
                runner.AssertUnchanged("Step3", "filter.status.value", b3.Status, s.Status);
                await AssertFullList(runner, "Step3", "S0_after_name", b3);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 4 — Code filter "BBHS" + Enter (keyboard search)
        // ════════════════════════════════════════════════════════════════
        var b4 = await ReadSnap();
        await runner.ExecuteStep("Step4", Page, $"Type '{CodeFilterValue}' in Code and press Enter",
            action: async () => await Try(runner, "Step4", async () =>
            {
                await FillFilter(CodeFilterSel, CodeFilterValue);
                await PressEnterAndWait(CodeFilterSel);
            }),
            assertions: async () =>
            {
                var s = await ReadSnap();
                var codes = await GetCodes();
                runner.Capture("S4_code", "grid.rowCount", s.Rows);
                runner.Capture("S4_code", "pager.info", s.Pager);
                runner.Capture("S4_code", "pager.total", s.Total);
                runner.Capture("S4_code", "grid.rowIds", s.Ids);
                runner.Capture("S4_code", "grid.column[Code].sequence", string.Join(",", codes));

                runner.AssertChanged("Step4", "filter.code.value", b4.Code, s.Code);
                runner.AssertEquals("Step4", "filter.code.value", s.Code, CodeFilterValue);
                runner.Assert("Step4", "grid.column[Code].allMatch",
                    codes.Count > 0 && codes.All(c => c.Contains(CodeFilterValue, StringComparison.OrdinalIgnoreCase)),
                    $"All {codes.Count} Codes contain '{CodeFilterValue}': {string.Join(", ", codes)}",
                    $"Not every Code contains '{CodeFilterValue}' ({codes.Count} rows): {string.Join(", ", codes)}");
                runner.AssertEquals("Step4", "grid.column[Code].sequence", string.Join(",", codes), "BBHS01");
                runner.AssertChanged("Step4", "pager.total", b4.Total, s.Total);
                runner.AssertEquals("Step4", "pager.total", s.Total, "1");
                runner.AssertChanged("Step4", "pager.info", b4.Pager, s.Pager);
                runner.AssertEquals("Step4", "pager.info", s.Pager, "View 1 - 1 of 1");
                runner.AssertEquals("Step4", "grid.rowCount", s.Rows, "1");
                runner.AssertChanged("Step4", "grid.rowIds", b4.Ids, s.Ids);
                runner.AssertEquals("Step4", "grid.rowIds", s.Ids, "2");
                runner.AssertUnchanged("Step4", "filter.name.value", b4.Name, s.Name);
                runner.AssertUnchanged("Step4", "filter.status.value", b4.Status, s.Status);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 5 — Clear (after Code filter)
        // ════════════════════════════════════════════════════════════════
        var b5 = await ReadSnap();
        await runner.ExecuteStep("Step5", Page, "Click Clear (after the Code filter)",
            action: async () => await Try(runner, "Step5", () => ClickAndWait(ClearBtnSel)),
            assertions: async () =>
            {
                var s = await ReadSnap();
                runner.AssertChanged("Step5", "filter.code.value", b5.Code, s.Code);
                runner.AssertChanged("Step5", "grid.rowIds", b5.Ids, s.Ids);
                runner.AssertChanged("Step5", "pager.total", b5.Total, s.Total);
                runner.AssertUnchanged("Step5", "filter.status.value", b5.Status, s.Status);
                await AssertFullList(runner, "Step5", "S0_after_code", b5);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 6 — Status = Inactive + Search
        // ════════════════════════════════════════════════════════════════
        var b6 = await ReadSnap();
        await runner.ExecuteStep("Step6", Page, "Set Status = Inactive and click Search",
            action: async () => await Try(runner, "Step6", async () =>
            {
                await SelectStatus(StatusInactiveText);
                await ClickAndWait(SearchBtnSel);
            }),
            assertions: async () =>
            {
                var s = await ReadSnap();
                var statuses = await GetStatusColumn();
                runner.Capture("S4_status", "grid.rowCount", s.Rows);
                runner.Capture("S4_status", "pager.info", s.Pager);
                runner.Capture("S4_status", "pager.total", s.Total);
                runner.Capture("S4_status", "grid.rowIds", s.Ids);
                runner.Capture("S4_status", "grid.column[Status].sequence", string.Join(",", statuses));

                runner.AssertChanged("Step6", "filter.status.value", b6.Status, s.Status);
                runner.AssertEquals("Step6", "filter.status.value", s.Status, "Inactive");
                runner.AssertEquals("Step6", "grid.column[Status].sequence", string.Join(",", statuses), "Inactive,Inactive");
                runner.AssertChanged("Step6", "pager.total", b6.Total, s.Total);
                runner.AssertEquals("Step6", "pager.total", s.Total, "2");
                runner.AssertEquals("Step6", "pager.info", s.Pager, "View 1 - 2 of 2");
                runner.AssertEquals("Step6", "grid.rowCount", s.Rows, "2");
                runner.AssertChanged("Step6", "grid.rowIds", b6.Ids, s.Ids);
                runner.AssertEquals("Step6", "grid.rowIds", s.Ids, "8,11");
                runner.AssertUnchanged("Step6", "filter.name.value", b6.Name, s.Name);
                runner.AssertUnchanged("Step6", "filter.code.value", b6.Code, s.Code);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 7 — Clear (after Status filter)
        // ════════════════════════════════════════════════════════════════
        var b7 = await ReadSnap();
        await runner.ExecuteStep("Step7", Page, "Click Clear (after the Status filter)",
            action: async () => await Try(runner, "Step7", () => ClickAndWait(ClearBtnSel)),
            assertions: async () =>
            {
                var s = await ReadSnap();
                runner.AssertChanged("Step7", "filter.status.value", b7.Status, s.Status);
                runner.AssertChanged("Step7", "grid.rowIds", b7.Ids, s.Ids);
                runner.AssertChanged("Step7", "pager.total", b7.Total, s.Total);
                await AssertFullList(runner, "Step7", "S0_after_status", b7);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW B1 STEP 1 — No-match filter (S5)
        // ════════════════════════════════════════════════════════════════
        var bb11 = await ReadSnap();
        var bb11Err = await IsErrorVisible();
        await runner.ExecuteStep("B1_Step1", Page, $"Type '{NoMatchValue}' in Payor Name and click Search",
            action: async () => await Try(runner, "B1_Step1", async () =>
            {
                await FillFilter(NameFilterSel, NoMatchValue);
                await ClickAndWait(SearchBtnSel);
            }),
            assertions: async () =>
            {
                var s = await ReadSnap();
                var err = await IsErrorVisible();
                var tbody = await GetTbodyStructure();
                runner.Capture("S5", "grid.rowCount", s.Rows);
                runner.Capture("S5", "pager.info", s.Pager);
                runner.Capture("S5", "grid.emptyMessage", s.Pager);
                runner.Capture("S5", "grid.tbody.structure", tbody);
                runner.Capture("S5", "error.visible", err);

                runner.AssertChanged("B1_Step1", "grid.rowCount", bb11.Rows, s.Rows);
                runner.AssertEquals("B1_Step1", "grid.rowCount", s.Rows, "0");
                runner.AssertChanged("B1_Step1", "pager.info", bb11.Pager, s.Pager);
                runner.Assert("B1_Step1", "grid.emptyMessage",
                    s.Pager == "View 0 - 0 of 0" || s.Pager.Contains("No records", StringComparison.OrdinalIgnoreCase),
                    $"Pager shows the empty-grid message '{s.Pager}'",
                    $"Pager '{s.Pager}' is not an empty-grid message");
                runner.AssertEquals("B1_Step1", "grid.tbody.structure", tbody, "jqgfirstrow");
                runner.AssertUnchanged("B1_Step1", "filter.code.value", bb11.Code, s.Code);
                runner.AssertUnchanged("B1_Step1", "filter.status.value", bb11.Status, s.Status);
                runner.AssertUnchanged("B1_Step1", "error.visible", bb11Err, err);
                runner.AssertUnchanged("B1_Step1", "saveButton.enabled", bb11.Save, s.Save);
            },
            gridSelector: "table.mud-table-root tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "target", "no_results"); } catch { }
        await runner.SaveSnapshot(Page, "S5", "no_results", "table.mud-table-root");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW B1 STEP 2 — Clear (after no-match)
        // ════════════════════════════════════════════════════════════════
        var bb12 = await ReadSnap();
        await runner.ExecuteStep("B1_Step2", Page, "Click Clear (after the no-match search)",
            action: async () => await Try(runner, "B1_Step2", () => ClickAndWait(ClearBtnSel)),
            assertions: async () =>
            {
                var s = await ReadSnap();
                runner.AssertChanged("B1_Step2", "filter.name.value", bb12.Name, s.Name);
                runner.AssertChanged("B1_Step2", "grid.rowCount", bb12.Rows, s.Rows);
                runner.AssertChanged("B1_Step2", "pager.info", bb12.Pager, s.Pager);
                await AssertFullList(runner, "B1_Step2", "S0_after_nomatch", bb12);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW B2 STEP 1 — Next page on the unfiltered list (S1)
        // ════════════════════════════════════════════════════════════════
        var bb21 = await ReadSnap();
        await runner.ExecuteStep("B2_Step1", Page, "Click Next page (unfiltered list)",
            action: async () => await Try(runner, "B2_Step1", () => ClickAndWait(NextSel)),
            assertions: async () =>
            {
                var s = await ReadSnap();
                runner.Capture("S1_page2", "pager.info", s.Pager);
                runner.Capture("S1_page2", "grid.rowIds", s.Ids);

                runner.AssertChanged("B2_Step1", "pager.info", bb21.Pager, s.Pager);
                runner.AssertEquals("B2_Step1", "pager.info", s.Pager, Page2Pager);
                runner.AssertChanged("B2_Step1", "grid.row[0].id", bb21.Row0Id, s.Row0Id);
                runner.AssertEquals("B2_Step1", "grid.row[0].id", s.Row0Id, Page2FirstId);
                runner.AssertUnchanged("B2_Step1", "pager.total", bb21.Total, s.Total);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW B2 STEP 2 — Status = Active + Search while on page 2
        // ════════════════════════════════════════════════════════════════
        var bb22 = await ReadSnap();
        await runner.ExecuteStep("B2_Step2", Page, "Set Status = Active and click Search while on page 2",
            action: async () => await Try(runner, "B2_Step2", async () =>
            {
                await SelectStatus(StatusActiveText);
                await ClickAndWait(SearchBtnSel);
            }),
            assertions: async () =>
            {
                var s = await ReadSnap();
                var statuses = await GetStatusColumn();
                var page = await GetCurrentPage();
                runner.Capture("S4_active", "pager.info", s.Pager);
                runner.Capture("S4_active", "pager.total", s.Total);
                runner.Capture("S4_active", "grid.rowIds", s.Ids);
                runner.Capture("S4_active", "grid.column[Status].sequence", string.Join(",", statuses));

                runner.AssertChanged("B2_Step2", "filter.status.value", bb22.Status, s.Status);
                runner.AssertEquals("B2_Step2", "filter.status.value", s.Status, "Active");
                runner.AssertChanged("B2_Step2", "pager.info", bb22.Pager, s.Pager);
                runner.AssertEquals("B2_Step2", "pager.info", s.Pager, ActivePager);
                runner.AssertEquals("B2_Step2", "pager.page", page, "1");
                runner.Assert("B2_Step2", "grid.column[Status].allActive",
                    statuses.Count > 0 && statuses.All(v => v == "Active"),
                    $"All {statuses.Count} rows show Active",
                    $"Not every row shows Active ({statuses.Count} rows): {string.Join(", ", statuses)}");
                runner.AssertChanged("B2_Step2", "pager.total", bb22.Total, s.Total);
                runner.AssertEquals("B2_Step2", "pager.total", s.Total, ActiveTotal);
                runner.AssertChanged("B2_Step2", "grid.rowIds", bb22.Ids, s.Ids);
                runner.AssertEquals("B2_Step2", "grid.rowIds", s.Ids, ActivePage1Ids);
                runner.AssertUnchanged("B2_Step2", "filter.name.value", bb22.Name, s.Name);
                runner.AssertUnchanged("B2_Step2", "filter.code.value", bb22.Code, s.Code);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW B2 STEP 3 — Clear (after Active filter)
        // ════════════════════════════════════════════════════════════════
        var bb23 = await ReadSnap();
        await runner.ExecuteStep("B2_Step3", Page, "Click Clear (after the Active filter)",
            action: async () => await Try(runner, "B2_Step3", () => ClickAndWait(ClearBtnSel)),
            assertions: async () =>
            {
                var s = await ReadSnap();
                runner.AssertChanged("B2_Step3", "filter.status.value", bb23.Status, s.Status);
                runner.AssertChanged("B2_Step3", "pager.total", bb23.Total, s.Total);
                runner.AssertChanged("B2_Step3", "grid.rowIds", bb23.Ids, s.Ids);
                await AssertFullList(runner, "B2_Step3", "S0_after_active", bb23);
            },
            gridSelector: "table.mud-table-root tbody");

        runner.Log("══════════════════════════════════════════════");
        runner.Log("TARGET MODULE B COMPLETE");
        runner.Log("══════════════════════════════════════════════");

        await runner.GenerateReport();

        if (runner.StepsFailed > 0 || runner.AssertionsFailed > 0)
            MarkIssueFound($"Module B target: {runner.StepsFailed} step(s), {runner.AssertionsFailed} assertion(s) failed vs reference baseline");
        else
            MarkVerified();
    }
}

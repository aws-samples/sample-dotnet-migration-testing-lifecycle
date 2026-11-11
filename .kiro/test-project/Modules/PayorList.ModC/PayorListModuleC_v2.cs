// plan-revision: 1; steps=9
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;
using MigrationTests.Playwright;
using MigrationTests.Playwright.Helpers;

namespace MigrationTests.Playwright.Tests;

/// <summary>
/// TARGET MODULE C — PayorList: Edit a cell and Save All (persist the batch). MUTATING.
///
/// Same human workflow as ReferencePayorListModuleC_v2, run against the TARGET system:
///   Blazor Server + MudBlazor MudDataGrid at /Pages/PayorList (port 5200).
///
/// Target DOM (PayorList.razor): click-to-edit cells are div.cell-display inside the column td;
/// editing renders a MudTextField / MudSelect in the same td. Dirty state is a MudAlert banner
/// "You have N unsaved change(s). ..." (no per-row highlight, so row[1].dirty = banner visible).
/// Save/Cancel feedback is a MudSnackbar. MudDataGrid rows have no DOM id: rows are located by
/// the ID column (td 0).
///
/// Expected EQUALS constants are the Reference constants verbatim; CHANGES / STAYS SAME
/// before-values are read from the Target at runtime. Differences are FINDINGS, not test bugs.
/// Actions catch their own errors (logged) so every step still records its assertions.
///
/// 9 steps (Step1..Step5, C1_Step1..C1_Step4) mirroring ReferencePayorListModuleC_v2.
/// </summary>
[TestFixture]
public class PayorListModuleC_v2 : TestBase
{
    private const string GridRowsSel = "tbody tr.mud-table-row";
    private const string AlertSel = ".mud-alert";
    private const string ChangeCountSel = "#changeCount";
    private const string SnackbarSel = ".mud-snackbar";
    private const string SaveBtnSel = "#btnSaveAll";
    private const string CancelBtnSel = "#btnCancelAll";

    private const string EditedName = "Acme Health Plans EDITED";
    private const string EditedCode = "AET999";
    private const string NewCategory = "Medicaid";

    // Column indexes (Reference column map 0:ID|1:Payor Name|2:Code|3:Category|4:Effective Date|5:Status).
    private const int NameCol = 1, CodeCol = 2, CategoryCol = 3, StatusCol = 5;

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

    /// <summary>The grid row whose ID cell (td 0) equals id; a never-matching locator when absent.</summary>
    private async Task<ILocator> Row(int id)
    {
        var rows = Page.Locator(GridRowsSel);
        int n = await rows.CountAsync();
        for (int i = 0; i < n; i++)
        {
            var t = (await rows.Nth(i).Locator("td").First.TextContentAsync())?.Trim() ?? "";
            if (t == id.ToString()) return rows.Nth(i);
        }
        return Page.Locator("tr[data-row-not-found]");
    }

    private async Task<ILocator> Cell(int id, int col) => (await Row(id)).Locator("td").Nth(col);

    private async Task<string> Text(ILocator el) =>
        await el.CountAsync() > 0 ? ((await el.First.TextContentAsync())?.Trim() ?? "") : "";

    private async Task<string> GetName(int id) => await Text(await Cell(id, NameCol));
    private async Task<string> GetCode(int id) => await Text(await Cell(id, CodeCol));
    private async Task<string> GetCategory(int id) => await Text(await Cell(id, CategoryCol));
    private async Task<string> GetStatusLabel(int id) => await Text(await Cell(id, StatusCol));

    private async Task<ILocator> EditInput(int id, int col) => (await Cell(id, col)).Locator("input");

    private async Task<bool> IsAlertVisible()
    {
        var el = Page.Locator(AlertSel);
        return await el.CountAsync() > 0 && await el.First.IsVisibleAsync();
    }

    /// <summary>Per-row unsaved-change marker: the MudDataGrid row carries class row-modified (RowClassFunc).</summary>
    private async Task<string> IsDirty(int id)
    {
        var row = await Row(id);
        if (await row.CountAsync() == 0) return "false";
        var cls = await row.First.GetAttributeAsync("class") ?? "";
        return cls.Split(' ').Contains("row-modified").ToString().ToLower();
    }

    private async Task<string> GetDirtyMessage() =>
        await Text(Page.Locator(ChangeCountSel)); // action-bar counter "N unsaved change(s)" / "No unsaved changes"

    private async Task<string> GetChangeCount()
    {
        var m = Regex.Match(await GetDirtyMessage(), @"(\d+)\s+unsaved");
        return m.Success ? m.Groups[1].Value : "0";
    }

    private async Task<string> GetMessage()
    {
        var bar = Page.Locator(SnackbarSel);
        if (await bar.CountAsync() == 0) return "";
        var content = bar.Last.Locator(".mud-snackbar-content-message");
        return await content.CountAsync() > 0 ? await Text(content) : await Text(bar.Last);
    }

    private async Task<string> IsEnabled(string sel)
    {
        var el = Page.Locator(sel).First;
        if (await el.CountAsync() == 0) return "false";
        return (await el.IsEnabledAsync()).ToString().ToLower();
    }

    private async Task<int> GetRowCount() => await Page.Locator(GridRowsSel).CountAsync();

    #endregion

    #region Action helpers

    private async Task Try(ModuleTestRunner runner, string step, Func<Task> act)
    {
        try { await act(); }
        catch (Exception e) { runner.Log($"[{step}] action error: {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
    }

    private async Task OpenCell(int id, int col)
    {
        await (await Cell(id, col)).Locator(".cell-display").First.ClickAsync(new() { Timeout = 5000 });
        await Page.WaitForTimeoutAsync(800);
    }

    private async Task FillAndBlur(int id, int col, string value)
    {
        var input = (await EditInput(id, col)).First;
        await input.WaitForAsync(new() { Timeout = 5000 });
        await input.FillAsync(value);
        await Page.WaitForTimeoutAsync(300);
        await input.BlurAsync();
        await Page.WaitForTimeoutAsync(800);
    }

    /// <summary>Click Save All, wait for the snackbar and return its text.</summary>
    private async Task<string> SaveAll()
    {
        await Page.Locator(SaveBtnSel).First.ClickAsync(new() { Timeout = 5000 });
        await Page.Locator(SnackbarSel).First.WaitForAsync(new() { Timeout = 10000, State = WaitForSelectorState.Visible });
        var msg = await GetMessage();
        await Page.WaitForTimeoutAsync(1000);
        return msg;
    }

    #endregion

    [Test]
    public async Task PayorList_ModC()
    {
        var runner = new ModuleTestRunner("PayorList", "C", "Target", 1);

        runner.Log("══════════════════════════════════════════════");
        runner.Log("TARGET MODULE C — PayorList: Edit + Save All (Blazor/MudDataGrid)");
        runner.Log("══════════════════════════════════════════════");

        if (!await NavigateToPage())
        {
            runner.StepFail("Step1", "ABORTED: navigation to target PayorList failed (grid rows never rendered)");
            await runner.GenerateReport();
            MarkIssueFound("Navigation failed — target grid did not load");
            return;
        }

        // ════════════════════════════════════════════════════════════════
        // STEP 1 — Click the Payor Name cell of ID 1 (reach S8)
        // ════════════════════════════════════════════════════════════════
        var s1EditVisibleBefore = (await (await EditInput(1, NameCol)).CountAsync() > 0).ToString().ToLower();
        var s1SaveBefore = await IsEnabled(SaveBtnSel);
        var s1CountBefore = await GetChangeCount();
        await runner.ExecuteStep("Step1", Page, "Click the Payor Name cell of ID 1",
            action: async () => await Try(runner, "Step1", () => OpenCell(1, NameCol)),
            assertions: async () =>
            {
                var input = await EditInput(1, NameCol);
                var visible = (await input.CountAsync() > 0 && await input.First.IsVisibleAsync()).ToString().ToLower();
                var value = visible == "true" ? await input.First.InputValueAsync() : "";
                runner.Capture("S8", "row[1].name.editControl.visible", visible);
                runner.Capture("S8", "editControl.value", value);
                runner.AssertChanged("Step1", "row[1].name.editControl.visible", s1EditVisibleBefore, visible);
                runner.AssertEquals("Step1", "editControl.value", value, "Acme Health Plans");
                runner.AssertUnchanged("Step1", "saveButton.enabled", s1SaveBefore, await IsEnabled(SaveBtnSel));
                runner.AssertUnchanged("Step1", "changeCount", s1CountBefore, await GetChangeCount());
            },
            gridSelector: "table.mud-table-root tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "target", "cell_edit_open"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 2 — Change the name and blur (reach S1)
        // ════════════════════════════════════════════════════════════════
        var s2Input = await EditInput(1, NameCol);
        var s2NameBefore = await s2Input.CountAsync() > 0 ? await s2Input.First.InputValueAsync() : await GetName(1);
        var s2DirtyBefore = await IsDirty(1);
        var s2CountBefore = await GetChangeCount();
        var s2MsgBefore = await GetDirtyMessage();
        var s2SaveBefore = await IsEnabled(SaveBtnSel);
        var s2CancelBefore = await IsEnabled(CancelBtnSel);
        var s2RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step2", Page, $"Change the name to '{EditedName}' and blur",
            action: async () => await Try(runner, "Step2", () => FillAndBlur(1, NameCol, EditedName)),
            assertions: async () =>
            {
                var name = await GetName(1);
                var dirty = await IsDirty(1);
                var count = await GetChangeCount();
                var dirtyMsg = await GetDirtyMessage();
                var save = await IsEnabled(SaveBtnSel);
                var cancel = await IsEnabled(CancelBtnSel);
                runner.Capture("S1", "row[1].name.text", name);
                runner.Capture("S1", "row[1].dirty", dirty);
                runner.Capture("S1", "changeCount", count);
                runner.Capture("S1", "dirty.message", dirtyMsg);
                runner.AssertChanged("Step2", "row[1].name.text", s2NameBefore, name);
                runner.AssertEquals("Step2", "row[1].name.text", name, EditedName);
                runner.AssertChanged("Step2", "row[1].dirty", s2DirtyBefore, dirty);
                runner.AssertChanged("Step2", "changeCount", s2CountBefore, count);
                runner.AssertEquals("Step2", "changeCount", count, "1");
                runner.AssertChanged("Step2", "dirty.message", s2MsgBefore, dirtyMsg);
                runner.AssertEquals("Step2", "dirty.message", dirtyMsg, "1 unsaved change(s)");
                runner.AssertChanged("Step2", "saveButton.enabled", s2SaveBefore, save);
                runner.AssertChanged("Step2", "cancelButton.enabled", s2CancelBefore, cancel);
                runner.AssertUnchanged("Step2", "grid.rowCount", s2RowsBefore, (await GetRowCount()).ToString());
            },
            gridSelector: "table.mud-table-root tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "target", "dirty"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 3 — Change the Code of ID 1 and blur
        // ════════════════════════════════════════════════════════════════
        var s3CodeBefore = await GetCode(1);
        var s3CountBefore = await GetChangeCount();
        var s3SaveBefore = await IsEnabled(SaveBtnSel);
        await runner.ExecuteStep("Step3", Page, $"Change the Code of ID 1 from ACM001 to '{EditedCode}' and blur",
            action: async () => await Try(runner, "Step3", async () =>
            {
                await OpenCell(1, CodeCol);
                await FillAndBlur(1, CodeCol, EditedCode);
            }),
            assertions: async () =>
            {
                var code = await GetCode(1);
                runner.Capture("S1", "row[1].code.text", code);
                runner.AssertChanged("Step3", "row[1].code.text", s3CodeBefore, code);
                runner.AssertEquals("Step3", "row[1].code.text", code, EditedCode);
                runner.AssertUnchanged("Step3", "changeCount", s3CountBefore, await GetChangeCount());
                runner.AssertUnchanged("Step3", "saveButton.enabled", s3SaveBefore, await IsEnabled(SaveBtnSel));
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 4 — Save All (reach S2)
        // ════════════════════════════════════════════════════════════════
        var s4MsgBefore = await GetMessage();
        var s4CountBefore = await GetChangeCount();
        var s4SaveBefore = await IsEnabled(SaveBtnSel);
        var s4CancelBefore = await IsEnabled(CancelBtnSel);
        var s4DirtyBefore = await IsDirty(1);
        var s4NameBefore = await GetName(1);
        var s4CodeBefore = await GetCode(1);
        string s4Msg = "";
        await runner.ExecuteStep("Step4", Page, "Click Save All",
            action: async () => await Try(runner, "Step4", async () => { s4Msg = await SaveAll(); }),
            assertions: async () =>
            {
                var count = await GetChangeCount();
                var name = await GetName(1);
                var code = await GetCode(1);
                runner.Capture("S2", "message.text", s4Msg);
                runner.Capture("S2", "changeCount", count);
                runner.Capture("S2", "row[1].name.text", name);
                runner.Capture("S2", "row[1].code.text", code);
                runner.AssertChanged("Step4", "message.text", s4MsgBefore, s4Msg);
                runner.AssertEquals("Step4", "message.text", s4Msg, "Successfully saved 1 payor(s).");
                runner.AssertChanged("Step4", "changeCount", s4CountBefore, count);
                runner.AssertEquals("Step4", "changeCount", count, "0");
                runner.AssertChanged("Step4", "saveButton.enabled", s4SaveBefore, await IsEnabled(SaveBtnSel));
                runner.AssertChanged("Step4", "cancelButton.enabled", s4CancelBefore, await IsEnabled(CancelBtnSel));
                runner.AssertChanged("Step4", "row[1].dirty", s4DirtyBefore, await IsDirty(1));
                runner.AssertUnchanged("Step4", "row[1].name.text", s4NameBefore, name);
                runner.AssertUnchanged("Step4", "row[1].code.text", s4CodeBefore, code);
            },
            gridSelector: "table.mud-table-root tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "target", "after_save"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 5 — Re-read ID 1 after a fresh page load (persistence)
        // ════════════════════════════════════════════════════════════════
        var s5NameBefore = await GetName(1);
        var s5CodeBefore = await GetCode(1);
        var s5CountBefore = await GetChangeCount();
        await runner.ExecuteStep("Step5", Page, "Re-read the ID 1 row (verify persistence)",
            action: async () => await Try(runner, "Step5", async () => { await NavigateToPage(); }),
            assertions: async () =>
            {
                var name = await GetName(1);
                var code = await GetCode(1);
                runner.Capture("S2_reload", "row[1].name.text", name);
                runner.Capture("S2_reload", "row[1].code.text", code);
                runner.AssertUnchanged("Step5", "row[1].name.text", s5NameBefore, name);
                runner.AssertUnchanged("Step5", "row[1].code.text", s5CodeBefore, code);
                runner.AssertUnchanged("Step5", "changeCount", s5CountBefore, await GetChangeCount());
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW C1 STEP 1 — Category of ID 2 -> Medicaid (MudSelect in the cell)
        // ════════════════════════════════════════════════════════════════
        var c1CatBefore = await GetCategory(2);
        var c1CountBefore = await GetChangeCount();
        await runner.ExecuteStep("C1_Step1", Page, $"Click the Category cell of ID 2 and pick '{NewCategory}'",
            action: async () => await Try(runner, "C1_Step1", async () =>
            {
                await OpenCell(2, CategoryCol);
                // The Category editor is a native <select class="edit-select"> (like the Reference).
                var sel = (await Cell(2, CategoryCol)).Locator("select.edit-select").First;
                await sel.WaitForAsync(new() { Timeout = 5000 });
                await sel.SelectOptionAsync(NewCategory);
                await Page.WaitForTimeoutAsync(800);
            }),
            assertions: async () =>
            {
                var cat = await GetCategory(2);
                var count = await GetChangeCount();
                runner.Capture("S1_c1", "row[2].category.text", cat);
                runner.Capture("S1_c1", "changeCount", count);
                runner.AssertChanged("C1_Step1", "row[2].category.text", c1CatBefore, cat);
                runner.AssertEquals("C1_Step1", "row[2].category.text", cat, NewCategory);
                runner.AssertChanged("C1_Step1", "changeCount", c1CountBefore, count);
                runner.AssertEquals("C1_Step1", "changeCount", count, "1");
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW C1 STEP 2 — Toggle Status of ID 2 -> Inactive (MudSwitch)
        // ════════════════════════════════════════════════════════════════
        var c2StatusBefore = await GetStatusLabel(2);
        var c2CountBefore = await GetChangeCount();
        await runner.ExecuteStep("C1_Step2", Page, "Toggle the Status of ID 2 to Inactive",
            action: async () => await Try(runner, "C1_Step2", async () =>
            {
                var cell = await Cell(2, StatusCol);
                await cell.Locator("input[type='checkbox']").First.ClickAsync(new() { Force = true, Timeout = 5000 });
                await Page.WaitForTimeoutAsync(800);
            }),
            assertions: async () =>
            {
                var status = await GetStatusLabel(2);
                runner.Capture("S1_c1", "row[2].status.label", status);
                runner.AssertChanged("C1_Step2", "row[2].status.label", c2StatusBefore, status);
                runner.AssertEquals("C1_Step2", "row[2].status.label", status, "Inactive");
                runner.AssertUnchanged("C1_Step2", "changeCount", c2CountBefore, await GetChangeCount());
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW C1 STEP 3 — Save All
        // ════════════════════════════════════════════════════════════════
        var c3MsgBefore = await GetMessage();
        var c3CountBefore = await GetChangeCount();
        var c3CatBefore = await GetCategory(2);
        var c3StatusBefore = await GetStatusLabel(2);
        string c3Msg = "";
        await runner.ExecuteStep("C1_Step3", Page, "Click Save All",
            action: async () => await Try(runner, "C1_Step3", async () => { c3Msg = await SaveAll(); }),
            assertions: async () =>
            {
                var count = await GetChangeCount();
                var cat = await GetCategory(2);
                var status = await GetStatusLabel(2);
                runner.Capture("S2_c1", "message.text", c3Msg);
                runner.Capture("S2_c1", "row[2].category.text", cat);
                runner.Capture("S2_c1", "row[2].status.label", status);
                runner.AssertChanged("C1_Step3", "message.text", c3MsgBefore, c3Msg);
                runner.AssertEquals("C1_Step3", "message.text", c3Msg, "Successfully saved 1 payor(s).");
                runner.AssertChanged("C1_Step3", "changeCount", c3CountBefore, count);
                runner.AssertEquals("C1_Step3", "changeCount", count, "0");
                runner.AssertUnchanged("C1_Step3", "row[2].category.text", c3CatBefore, cat);
                runner.AssertUnchanged("C1_Step3", "row[2].status.label", c3StatusBefore, status);
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW C1 STEP 4 — Re-read ID 2 after a fresh page load (persistence)
        // ════════════════════════════════════════════════════════════════
        var c4CatBefore = await GetCategory(2);
        var c4StatusBefore = await GetStatusLabel(2);
        var c4CountBefore = await GetChangeCount();
        await runner.ExecuteStep("C1_Step4", Page, "Reload the page and re-read row ID 2 (verify persistence)",
            action: async () => await Try(runner, "C1_Step4", async () => { await NavigateToPage(); }),
            assertions: async () =>
            {
                var cat = await GetCategory(2);
                var status = await GetStatusLabel(2);
                runner.Capture("S2_c1_reload", "row[2].category.text", cat);
                runner.Capture("S2_c1_reload", "row[2].status.label", status);
                runner.AssertUnchanged("C1_Step4", "row[2].category.text", c4CatBefore, cat);
                runner.AssertUnchanged("C1_Step4", "row[2].status.label", c4StatusBefore, status);
                runner.AssertUnchanged("C1_Step4", "changeCount", c4CountBefore, await GetChangeCount());
            },
            gridSelector: "table.mud-table-root tbody");

        runner.Log("══════════════════════════════════════════════");
        runner.Log("TARGET MODULE C COMPLETE");
        runner.Log("══════════════════════════════════════════════");

        await runner.GenerateReport();

        if (runner.StepsFailed > 0 || runner.AssertionsFailed > 0)
            MarkIssueFound($"Module C target: {runner.StepsFailed} step(s), {runner.AssertionsFailed} assertion(s) failed vs reference baseline");
        else
            MarkVerified();
    }
}

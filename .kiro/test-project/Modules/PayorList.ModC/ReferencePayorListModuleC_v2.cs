// plan-revision: 1; steps=9
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;
using MigrationTests.Playwright;
using MigrationTests.Playwright.Helpers;

namespace MigrationTests.Playwright.Tests;

/// <summary>
/// REFERENCE MODULE C — PayorList: Edit a cell and Save All (persist the batch). MUTATING.
///
/// User story (plan.json): "I correct a payor's name and code in the grid, see the row flagged as
/// unsaved, click Save All and confirm the change stuck; then I change a category and status and
/// save again."
///
/// Reference system = ASP.NET WebForms + free-jqGrid at /Pages/PayorList.aspx (port 5100).
/// Click-to-edit cells: span.cell-name / span.cell-code swap to input.edit-*-input; blur stores the
/// value and marks the row (tr.row-modified) when it differs from the loaded value. The action bar
/// text #changeCount shows "No unsaved changes" / "N unsaved change(s)". Save All posts
/// BatchUpdatePayors, shows the #toast "Successfully saved N payor(s)." and reloads the grid.
///
/// Saves mutate PayorContracts.Host's in-memory list. The runner restarts PayorContracts.Host after this test
/// (dataEffect = mutating), so the test does NOT reset data itself.
///
/// 5 main steps + sub-flow C1 (4 steps) = 9 steps (plan.json modules[C].steps).
/// </summary>
[TestFixture]
public class ReferencePayorListModuleC_v2 : TestBase
{
    private const string GridRowsSel = "#payorGrid tr.jqgrow";
    private const string ChangeCountSel = "#changeCount";
    private const string SaveBtnSel = "#btnSaveAll";
    private const string CancelBtnSel = "#btnCancelAll";
    private const string ToastSel = "#toast";

    private const string EditedName = "Acme Health Plans EDITED";
    private const string EditedCode = "AET999";
    private const string NewCategory = "Medicaid";

    #region Navigation

    private async Task<bool> NavigateToPage()
    {
        await Page.GotoAsync($"{Settings.ReferenceBaseUrl}/Pages/PayorList.aspx");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AssertNotRedirectedToAuth();
        try
        {
            await Page.WaitForSelectorAsync(GridRowsSel,
                new() { Timeout = 20000, State = WaitForSelectorState.Visible });
            await Page.WaitForTimeoutAsync(1000);
        }
        catch (TimeoutException)
        {
            await CaptureDiagnosticHtmlAsync(GridRowsSel, "grid rows never appeared");
            return false;
        }
        return Page.Url.Contains("PayorList", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Read helpers

    private ILocator Row(int id) => Page.Locator($"#payorGrid tr.jqgrow[id='{id}']");

    private async Task<string> Text(ILocator el) =>
        await el.CountAsync() > 0 ? ((await el.First.TextContentAsync())?.Trim() ?? "") : "";

    private async Task<string> GetName(int id) => await Text(Row(id).Locator("span.cell-name"));
    private async Task<string> GetCode(int id) => await Text(Row(id).Locator("span.cell-code"));
    private async Task<string> GetCategory(int id) => await Text(Row(id).Locator("span.cell-category"));
    private async Task<string> GetStatusLabel(int id) =>
        await Text(Row(id).Locator("td[aria-describedby='payorGrid_isActive'] label"));

    private async Task<string> IsDirty(int id)
    {
        var cls = await Row(id).First.GetAttributeAsync("class") ?? "";
        return cls.Split(' ').Contains("row-modified").ToString().ToLower();
    }

    private async Task<string> GetDirtyMessage() => await Text(Page.Locator(ChangeCountSel));

    /// <summary>Number of unsaved rows parsed from the dirty text ("N unsaved change(s)" -> N, otherwise 0).</summary>
    private async Task<string> GetChangeCount()
    {
        var m = Regex.Match(await GetDirtyMessage(), @"(\d+)\s+unsaved");
        return m.Success ? m.Groups[1].Value : "0";
    }

    private async Task<string> GetMessage() => await Text(Page.Locator(ToastSel));

    private async Task<string> IsEnabled(string sel) =>
        (await Page.Locator(sel).First.IsEnabledAsync()).ToString().ToLower();

    private async Task<int> GetRowCount() => await Page.Locator(GridRowsSel).CountAsync();

    #endregion

    #region Action helpers

    private async Task EditText(int id, string cellClass, string inputClass, string value)
    {
        await Row(id).Locator($"span.{cellClass}").First.ClickAsync();
        var input = Row(id).Locator($"input.{inputClass}").First;
        await input.WaitForAsync(new() { Timeout = 5000 });
        await input.FillAsync(value);
        await input.BlurAsync();
        await Page.WaitForTimeoutAsync(500);
    }

    /// <summary>Click Save All, wait for the toast and return its text; then let the grid reload settle.</summary>
    private async Task<string> SaveAll()
    {
        await Page.Locator(SaveBtnSel).ClickAsync();
        await Page.Locator($"{ToastSel}.toast-success, {ToastSel}.toast-error")
            .First.WaitForAsync(new() { Timeout = 10000, State = WaitForSelectorState.Visible });
        var msg = await GetMessage();
        await Page.WaitForTimeoutAsync(2000); // reloadGrid settle
        return msg;
    }

    #endregion

    [Test]
    public async Task ReferencePayorList_ModC()
    {
        var runner = new ModuleTestRunner("PayorList", "C", "Reference", 0);

        runner.Log("══════════════════════════════════════════════");
        runner.Log("REFERENCE MODULE C — PayorList: Edit + Save All");
        runner.Log("══════════════════════════════════════════════");

        if (!await NavigateToPage())
        {
            runner.StepFail("Step1", "ABORTED: navigation to reference PayorList failed (grid rows never rendered)");
            await runner.GenerateReport();
            MarkIssueFound("Navigation failed — reference grid did not load");
            return;
        }

        // ════════════════════════════════════════════════════════════════
        // STEP 1 — Click the Payor Name cell of ID 1 (reach S8)
        // ════════════════════════════════════════════════════════════════
        var editInput = Row(1).Locator("input.edit-name-input");
        var s1EditVisibleBefore = (await editInput.CountAsync() > 0).ToString().ToLower();
        var s1SaveBefore = await IsEnabled(SaveBtnSel);
        var s1CountBefore = await GetChangeCount();
        await runner.ExecuteStep("Step1", Page, "Click the Payor Name cell of ID 1",
            action: async () =>
            {
                await Row(1).Locator("span.cell-name").First.ClickAsync();
                await editInput.First.WaitForAsync(new() { Timeout = 5000 });
            },
            assertions: async () =>
            {
                var visible = (await editInput.CountAsync() > 0 && await editInput.First.IsVisibleAsync()).ToString().ToLower();
                var value = visible == "true" ? await editInput.First.InputValueAsync() : "";
                runner.Capture("S8", "row[1].name.editControl.visible", visible);
                runner.Capture("S8", "editControl.value", value);
                runner.AssertChanged("Step1", "row[1].name.editControl.visible", s1EditVisibleBefore, visible);
                runner.AssertEquals("Step1", "editControl.value", value, "Acme Health Plans");
                runner.AssertUnchanged("Step1", "saveButton.enabled", s1SaveBefore, await IsEnabled(SaveBtnSel));
                runner.AssertUnchanged("Step1", "changeCount", s1CountBefore, await GetChangeCount());
            },
            gridSelector: "#payorGrid tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "reference", "cell_edit_open"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 2 — Change the name and blur (reach S1)
        // ════════════════════════════════════════════════════════════════
        var s2NameBefore = await editInput.First.InputValueAsync();
        var s2DirtyBefore = await IsDirty(1);
        var s2CountBefore = await GetChangeCount();
        var s2MsgBefore = await GetDirtyMessage();
        var s2SaveBefore = await IsEnabled(SaveBtnSel);
        var s2CancelBefore = await IsEnabled(CancelBtnSel);
        var s2RowsBefore = (await GetRowCount()).ToString();
        await runner.ExecuteStep("Step2", Page, $"Change the name to '{EditedName}' and blur",
            action: async () =>
            {
                await editInput.First.FillAsync(EditedName);
                await editInput.First.BlurAsync();
                await Page.WaitForTimeoutAsync(500);
            },
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
            gridSelector: "#payorGrid tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "reference", "dirty"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 3 — Change the Code of ID 1 and blur
        // ════════════════════════════════════════════════════════════════
        var s3CodeBefore = await GetCode(1);
        var s3CountBefore = await GetChangeCount();
        var s3SaveBefore = await IsEnabled(SaveBtnSel);
        await runner.ExecuteStep("Step3", Page, $"Change the Code of ID 1 from ACM001 to '{EditedCode}' and blur",
            action: async () => await EditText(1, "cell-code", "edit-code-input", EditedCode),
            assertions: async () =>
            {
                var code = await GetCode(1);
                runner.Capture("S1", "row[1].code.text", code);
                runner.AssertChanged("Step3", "row[1].code.text", s3CodeBefore, code);
                runner.AssertEquals("Step3", "row[1].code.text", code, EditedCode);
                runner.AssertUnchanged("Step3", "changeCount", s3CountBefore, await GetChangeCount());
                runner.AssertUnchanged("Step3", "saveButton.enabled", s3SaveBefore, await IsEnabled(SaveBtnSel));
            },
            gridSelector: "#payorGrid tbody");

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
            action: async () => { s4Msg = await SaveAll(); },
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
            gridSelector: "#payorGrid tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "reference", "after_save"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 5 — Re-read ID 1 after a fresh page load (persistence)
        // ════════════════════════════════════════════════════════════════
        var s5NameBefore = await GetName(1);
        var s5CodeBefore = await GetCode(1);
        var s5CountBefore = await GetChangeCount();
        await runner.ExecuteStep("Step5", Page, "Re-read the ID 1 row (verify persistence)",
            action: async () => { await NavigateToPage(); },
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW C1 STEP 1 — Category of ID 2 -> Medicaid
        // ════════════════════════════════════════════════════════════════
        var c1CatBefore = await GetCategory(2);
        var c1CountBefore = await GetChangeCount();
        await runner.ExecuteStep("C1_Step1", Page, $"Click the Category cell of ID 2 and pick '{NewCategory}'",
            action: async () =>
            {
                await Row(2).Locator("span.cell-category").First.ClickAsync();
                var sel = Row(2).Locator("select.edit-category-input").First;
                await sel.WaitForAsync(new() { Timeout = 5000 });
                await sel.SelectOptionAsync(NewCategory);
                await Page.WaitForTimeoutAsync(500);
            },
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW C1 STEP 2 — Toggle Status of ID 2 -> Inactive
        // ════════════════════════════════════════════════════════════════
        var c2StatusBefore = await GetStatusLabel(2);
        var c2CountBefore = await GetChangeCount();
        await runner.ExecuteStep("C1_Step2", Page, "Toggle the Status of ID 2 to Inactive",
            action: async () =>
            {
                await Row(2).Locator("input.edit-status").First.ClickAsync();
                await Page.WaitForTimeoutAsync(500);
            },
            assertions: async () =>
            {
                var status = await GetStatusLabel(2);
                runner.Capture("S1_c1", "row[2].status.label", status);
                runner.AssertChanged("C1_Step2", "row[2].status.label", c2StatusBefore, status);
                runner.AssertEquals("C1_Step2", "row[2].status.label", status, "Inactive");
                runner.AssertUnchanged("C1_Step2", "changeCount", c2CountBefore, await GetChangeCount());
            },
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW C1 STEP 3 — Save All
        // ════════════════════════════════════════════════════════════════
        var c3MsgBefore = await GetMessage();
        var c3CountBefore = await GetChangeCount();
        var c3CatBefore = await GetCategory(2);
        var c3StatusBefore = await GetStatusLabel(2);
        string c3Msg = "";
        await runner.ExecuteStep("C1_Step3", Page, "Click Save All",
            action: async () => { c3Msg = await SaveAll(); },
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW C1 STEP 4 — Re-read ID 2 after a fresh page load (persistence)
        // ════════════════════════════════════════════════════════════════
        var c4CatBefore = await GetCategory(2);
        var c4StatusBefore = await GetStatusLabel(2);
        var c4CountBefore = await GetChangeCount();
        await runner.ExecuteStep("C1_Step4", Page, "Reload the page and re-read row ID 2 (verify persistence)",
            action: async () => { await NavigateToPage(); },
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
            gridSelector: "#payorGrid tbody");

        runner.Log("══════════════════════════════════════════════");
        runner.Log("REFERENCE MODULE C COMPLETE");
        runner.Log("══════════════════════════════════════════════");

        await runner.GenerateReport();

        if (runner.StepsFailed > 0 || runner.AssertionsFailed > 0)
            MarkIssueFound($"Module C reference: {runner.StepsFailed} step(s), {runner.AssertionsFailed} assertion(s) failed");
        else
            MarkVerified();
    }
}

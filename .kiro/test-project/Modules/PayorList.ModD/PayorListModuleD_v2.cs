// plan-revision: 1; steps=6
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;
using MigrationTests.Playwright;
using MigrationTests.Playwright.Helpers;

namespace MigrationTests.Playwright.Tests;

/// <summary>
/// TARGET MODULE D — PayorList: Edit a cell and Cancel (discard the batch). READ-ONLY.
///
/// Same human workflow as ReferencePayorListModuleD_v2, run against the TARGET system:
///   Blazor Server + MudBlazor MudDataGrid at /Pages/PayorList (port 5200).
///
/// Target DOM (PayorList.razor): click-to-edit div.cell-display swaps to a MudTextField; the dirty
/// state is the MudAlert banner "You have N unsaved change(s). ..."; Cancel restores the loaded
/// values in memory and shows a MudSnackbar "Changes discarded.". Rows are located by the ID cell.
///
/// Expected EQUALS constants are the Reference constants verbatim; CHANGES / STAYS SAME
/// before-values are read from the Target at runtime. Actions catch their own errors (logged).
///
/// 6 steps (Step1..Step3, D1_Step1, D1_Step2, D2_Step1) mirroring ReferencePayorListModuleD_v2.
/// CSS state (plan cssStates): after_cancel, right after the Step2 Cancel click.
/// </summary>
[TestFixture]
public class PayorListModuleD_v2 : TestBase
{
    private const string GridRowsSel = "tbody tr.mud-table-row";
    private const string AlertSel = ".mud-alert";
    private const string ChangeCountSel = "#changeCount";
    private const string SnackbarSel = ".mud-snackbar";
    private const string SaveBtnSel = "#btnSaveAll";
    private const string CancelBtnSel = "#btnCancelAll";
    private const string PagerRangeSel = ".mud-table-pagination-caption";

    private const string EditedName3 = "Citrine EDITED";
    private const string EditedName4 = "Unity Crest EDITED";
    private const string EditedCode5 = "HUM999";
    private const string EscapedName6 = "Kestrel ESCAPED";

    private const int NameCol = 1, CodeCol = 2;
    private const int CleanRowId = 1;

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

    private async Task<bool> IsAlertVisible()
    {
        var el = Page.Locator(AlertSel);
        return await el.CountAsync() > 0 && await el.First.IsVisibleAsync();
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

    private async Task<string> GetSaveText() => await Text(Page.Locator(SaveBtnSel));

    /// <summary>MudDataGridPager: the last .mud-table-pagination-caption holds the range text.</summary>
    private async Task<string> GetPagerInfo()
    {
        var caps = Page.Locator(PagerRangeSel);
        int n = await caps.CountAsync();
        if (n == 0) return "";
        return (await caps.Nth(n - 1).TextContentAsync())?.Trim() ?? "";
    }

    private async Task<string> RowBackground(ILocator row) =>
        await row.CountAsync() == 0 ? "" :
            await row.First.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");

    /// <summary>
    /// Unsaved-change highlight: the row has no open editor and carries the app's row-modified class
    /// (no ref-* fix-round classes).
    /// </summary>
    private async Task<bool> IsHighlighted(int id)
    {
        var row = await Row(id);
        if (await row.CountAsync() == 0) return false;
        // Open inline editor only (every row also holds the Status checkbox input).
        if (await row.Locator("input.edit-input, select.edit-select").CountAsync() > 0) return false;
        // The page marks unsaved rows with class row-modified (RowClassFunc), like the Reference. A background
        // comparison is not used: MudDataGrid Hover tints the row under the mouse (the row just edited).
        var cls = await row.First.GetAttributeAsync("class") ?? "";
        return cls.Split(' ').Contains("row-modified");
    }

    /// <summary>Inline Payor Name editor (MudTextField input) open in the row.</summary>
    private async Task<bool> IsNameEditorVisible(int id)
    {
        var input = (await Cell(id, NameCol)).Locator("input");
        return await input.CountAsync() > 0 && await input.First.IsVisibleAsync();
    }

    #endregion

    #region Action helpers

    private async Task Try(ModuleTestRunner runner, string step, Func<Task> act)
    {
        try { await act(); }
        catch (Exception e) { runner.Log($"[{step}] action error: {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
    }

    private async Task EditText(int id, int col, string value)
    {
        await (await Cell(id, col)).Locator(".cell-display").First.ClickAsync(new() { Timeout = 5000 });
        await Page.WaitForTimeoutAsync(800);
        var input = (await Cell(id, col)).Locator("input").First;
        await input.WaitForAsync(new() { Timeout = 5000 });
        await input.FillAsync(value);
        await Page.WaitForTimeoutAsync(300);
        await input.BlurAsync();
        await Page.WaitForTimeoutAsync(800);
    }

    /// <summary>Click Cancel, wait for the snackbar and return its text.</summary>
    private async Task<string> CancelAll()
    {
        await Page.Locator(CancelBtnSel).First.ClickAsync(new() { Timeout = 5000 });
        await Page.Locator(SnackbarSel).First.WaitForAsync(new() { Timeout = 10000, State = WaitForSelectorState.Visible });
        var msg = await GetMessage();
        await Page.WaitForTimeoutAsync(1000);
        return msg;
    }

    /// <summary>Open the Payor Name editor, type a value and press Escape (no blur call).</summary>
    private async Task EditNameAndEscape(int id, string value)
    {
        await (await Cell(id, NameCol)).Locator(".cell-display").First.ClickAsync(new() { Timeout = 5000 });
        await Page.WaitForTimeoutAsync(800);
        var input = (await Cell(id, NameCol)).Locator("input").First;
        await input.WaitForAsync(new() { Timeout = 5000 });
        await input.FillAsync(value);
        await Page.WaitForTimeoutAsync(300);
        await input.PressAsync("Escape");
        await Page.WaitForTimeoutAsync(800);
    }

    #endregion

    [Test]
    public async Task PayorList_ModD()
    {
        var runner = new ModuleTestRunner("PayorList", "D", "Target", 1);

        runner.Log("══════════════════════════════════════════════");
        runner.Log("TARGET MODULE D — PayorList: Edit + Cancel (Blazor/MudDataGrid)");
        runner.Log("══════════════════════════════════════════════");

        if (!await NavigateToPage())
        {
            runner.StepFail("Step1", "ABORTED: navigation to target PayorList failed (grid rows never rendered)");
            await runner.GenerateReport();
            MarkIssueFound("Navigation failed — target grid did not load");
            return;
        }

        // ════════════════════════════════════════════════════════════════
        // STEP 1 — Edit Name of ID 3 and blur (reach S1)
        // ════════════════════════════════════════════════════════════════
        var s1NameBefore = await GetName(3);
        var s1CountBefore = await GetChangeCount();
        var s1SaveBefore = await IsEnabled(SaveBtnSel);
        var s1CancelBefore = await IsEnabled(CancelBtnSel);
        await runner.ExecuteStep("Step1", Page, $"Change the Payor Name of ID 3 to '{EditedName3}' and blur",
            action: async () => await Try(runner, "Step1", () => EditText(3, NameCol, EditedName3)),
            assertions: async () =>
            {
                var name = await GetName(3);
                var count = await GetChangeCount();
                runner.Capture("S1", "row[3].name.text", name);
                runner.Capture("S1", "changeCount", count);
                runner.Capture("S1", "dirty.message", await GetDirtyMessage());
                runner.AssertChanged("Step1", "row[3].name.text", s1NameBefore, name);
                runner.AssertEquals("Step1", "row[3].name.text", name, EditedName3);
                runner.AssertChanged("Step1", "changeCount", s1CountBefore, count);
                runner.AssertEquals("Step1", "changeCount", count, "1");
                var hl = await IsHighlighted(3);
                runner.Capture("S1", "row[3].highlighted", hl.ToString().ToLower());
                runner.Assert("Step1", "row[3].highlighted", hl,
                    "Row ID 3 is highlighted as changed", "Row ID 3 is NOT highlighted after the edit");
                var save = await IsEnabled(SaveBtnSel);
                var cancel = await IsEnabled(CancelBtnSel);
                runner.AssertChanged("Step1", "saveButton.enabled", s1SaveBefore, save);
                runner.AssertEquals("Step1", "saveButton.enabled", save, "true");
                runner.AssertChanged("Step1", "cancelButton.enabled", s1CancelBefore, cancel);
                runner.AssertEquals("Step1", "cancelButton.enabled", cancel, "true");
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // STEP 2 — Cancel (reach S3)
        // ════════════════════════════════════════════════════════════════
        var s2MsgBefore = await GetMessage();
        var s2NameBefore = await GetName(3);
        var s2CountBefore = await GetChangeCount();
        var s2SaveBefore = await IsEnabled(SaveBtnSel);
        var s2CancelBefore = await IsEnabled(CancelBtnSel);
        var s2PagerBefore = await GetPagerInfo();
        string s2Msg = "";
        await runner.ExecuteStep("Step2", Page, "Click Cancel",
            action: async () => await Try(runner, "Step2", async () => { s2Msg = await CancelAll(); }),
            assertions: async () =>
            {
                var name = await GetName(3);
                var count = await GetChangeCount();
                runner.Capture("S3", "message.text", s2Msg);
                runner.Capture("S3", "row[3].name.text", name);
                runner.Capture("S3", "changeCount", count);
                runner.AssertChanged("Step2", "message.text", s2MsgBefore, s2Msg);
                runner.AssertEquals("Step2", "message.text", s2Msg, "Changes discarded.");
                runner.AssertChanged("Step2", "row[3].name.text", s2NameBefore, name);
                runner.AssertEquals("Step2", "row[3].name.text", name, "Citrine Valley Healthcare");
                runner.AssertChanged("Step2", "changeCount", s2CountBefore, count);
                runner.AssertEquals("Step2", "changeCount", count, "0");
                var hl = await IsHighlighted(3);
                runner.Capture("S3", "row[3].highlighted", hl.ToString().ToLower());
                runner.Assert("Step2", "row[3].highlighted", !hl,
                    "Change highlight removed from row ID 3", "Row ID 3 is still highlighted after Cancel");
                var save = await IsEnabled(SaveBtnSel);
                var cancel = await IsEnabled(CancelBtnSel);
                runner.AssertChanged("Step2", "saveButton.enabled", s2SaveBefore, save);
                runner.AssertEquals("Step2", "saveButton.enabled", save, "false");
                runner.AssertChanged("Step2", "cancelButton.enabled", s2CancelBefore, cancel);
                runner.AssertEquals("Step2", "cancelButton.enabled", cancel, "false");
                var pager = await GetPagerInfo();
                runner.Capture("S3", "pager.info", pager);
                runner.AssertUnchanged("Step2", "pager.info", s2PagerBefore, pager);
            },
            gridSelector: "table.mud-table-root tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "target", "after_cancel"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 3 — Re-read ID 3 after a fresh page load (NOT persisted)
        // ════════════════════════════════════════════════════════════════
        var s3NameBefore = await GetName(3);
        var s3CountBefore = await GetChangeCount();
        await runner.ExecuteStep("Step3", Page, "Re-read the ID 3 row (verify NOT persisted)",
            action: async () => await Try(runner, "Step3", async () => { await NavigateToPage(); }),
            assertions: async () =>
            {
                var name = await GetName(3);
                runner.Capture("S3_reload", "row[3].name.text", name);
                runner.AssertUnchanged("Step3", "row[3].name.text", s3NameBefore, name);
                runner.AssertUnchanged("Step3", "changeCount", s3CountBefore, await GetChangeCount());
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW D1 STEP 1 — Edit Name of ID 4 and Code of ID 5
        // ════════════════════════════════════════════════════════════════
        var d1CountBefore = await GetChangeCount();
        var d1MsgBefore = await GetDirtyMessage();
        var d1Name4Before = await GetName(4);
        var d1Code5Before = await GetCode(5);
        var d1SaveTextBefore = await GetSaveText();
        await runner.ExecuteStep("D1_Step1", Page, $"Edit the Name of ID 4 ('{EditedName4}') and the Code of ID 5 ('{EditedCode5}')",
            action: async () => await Try(runner, "D1_Step1", async () =>
            {
                await EditText(4, NameCol, EditedName4);
                await EditText(5, CodeCol, EditedCode5);
            }),
            assertions: async () =>
            {
                var count = await GetChangeCount();
                var dirtyMsg = await GetDirtyMessage();
                var name4 = await GetName(4);
                var code5 = await GetCode(5);
                runner.Capture("S1_d1", "row[4].name.text", name4);
                runner.Capture("S1_d1", "row[5].code.text", code5);
                runner.Capture("S1_d1", "changeCount", count);
                runner.Capture("S1_d1", "dirty.message", dirtyMsg);
                runner.AssertChanged("D1_Step1", "row[4].name.text", d1Name4Before, name4);
                runner.AssertEquals("D1_Step1", "row[4].name.text", name4, EditedName4);
                runner.AssertChanged("D1_Step1", "row[5].code.text", d1Code5Before, code5);
                runner.AssertEquals("D1_Step1", "row[5].code.text", code5, EditedCode5);
                var hl4 = await IsHighlighted(4);
                var hl5 = await IsHighlighted(5);
                runner.Capture("S1_d1", "row[4].highlighted", hl4.ToString().ToLower());
                runner.Capture("S1_d1", "row[5].highlighted", hl5.ToString().ToLower());
                runner.Assert("D1_Step1", "row[4].highlighted", hl4,
                    "Row ID 4 is highlighted as changed", "Row ID 4 is NOT highlighted after the edit");
                runner.Assert("D1_Step1", "row[5].highlighted", hl5,
                    "Row ID 5 is highlighted as changed", "Row ID 5 is NOT highlighted after the edit");
                runner.AssertChanged("D1_Step1", "changeCount", d1CountBefore, count);
                runner.AssertEquals("D1_Step1", "changeCount", count, "2");
                runner.AssertChanged("D1_Step1", "dirty.message", d1MsgBefore, dirtyMsg);
                runner.AssertEquals("D1_Step1", "dirty.message", dirtyMsg, "2 unsaved change(s)");
                var saveText = await GetSaveText();
                runner.Capture("S1_d1", "saveButton.text", saveText);
                runner.AssertChanged("D1_Step1", "saveButton.text", d1SaveTextBefore, saveText);
                runner.AssertEquals("D1_Step1", "saveButton.text", saveText, "Save All (2)");
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW D1 STEP 2 — Cancel
        // ════════════════════════════════════════════════════════════════
        var d2MsgBefore = await GetMessage();
        var d2NameBefore = await GetName(4);
        var d2CodeBefore = await GetCode(5);
        var d2CountBefore = await GetChangeCount();
        string d2Msg = "";
        await runner.ExecuteStep("D1_Step2", Page, "Click Cancel",
            action: async () => await Try(runner, "D1_Step2", async () => { d2Msg = await CancelAll(); }),
            assertions: async () =>
            {
                var name = await GetName(4);
                var code = await GetCode(5);
                var count = await GetChangeCount();
                runner.Capture("S3_d1", "message.text", d2Msg);
                runner.Capture("S3_d1", "row[4].name.text", name);
                runner.Capture("S3_d1", "row[5].code.text", code);
                runner.AssertChanged("D1_Step2", "message.text", d2MsgBefore, d2Msg);
                runner.AssertEquals("D1_Step2", "message.text", d2Msg, "Changes discarded.");
                runner.AssertChanged("D1_Step2", "row[4].name.text", d2NameBefore, name);
                runner.AssertEquals("D1_Step2", "row[4].name.text", name, "Unity Crest Healthcare");
                runner.AssertChanged("D1_Step2", "row[5].code.text", d2CodeBefore, code);
                runner.AssertEquals("D1_Step2", "row[5].code.text", code, "HBR001");
                runner.AssertChanged("D1_Step2", "changeCount", d2CountBefore, count);
                runner.AssertEquals("D1_Step2", "changeCount", count, "0");
                runner.AssertEquals("D1_Step2", "saveButton.enabled", await IsEnabled(SaveBtnSel), "false");
                runner.AssertEquals("D1_Step2", "cancelButton.enabled", await IsEnabled(CancelBtnSel), "false");
            },
            gridSelector: "table.mud-table-root tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW D2 STEP 1 — Name of ID 6: type a value, press Escape
        // ════════════════════════════════════════════════════════════════
        var e1NameBefore = await GetName(6);
        var e1HlBefore = (await IsHighlighted(6)).ToString().ToLower();
        var e1CountBefore = await GetChangeCount();
        var e1SaveBefore = await IsEnabled(SaveBtnSel);
        await runner.ExecuteStep("D2_Step1", Page, $"Click the Payor Name of ID 6, type '{EscapedName6}' and press Escape",
            action: async () => await Try(runner, "D2_Step1", () => EditNameAndEscape(6, EscapedName6)),
            assertions: async () =>
            {
                var editor = await IsNameEditorVisible(6);
                var name = await GetName(6);
                var hl = await IsHighlighted(6);
                var count = await GetChangeCount();
                var save = await IsEnabled(SaveBtnSel);
                runner.Capture("S9_d2", "row[6].editor.visible", editor.ToString().ToLower());
                runner.Capture("S9_d2", "row[6].name.text", name);
                runner.Capture("S9_d2", "row[6].highlighted", hl.ToString().ToLower());
                runner.Capture("S9_d2", "changeCount", count);
                runner.Assert("D2_Step1", "row[6].editor.visible", !editor,
                    "The inline editor closed on Escape", "The inline editor is still open after Escape");
                runner.AssertUnchanged("D2_Step1", "row[6].name.text", e1NameBefore, name);
                runner.AssertEquals("D2_Step1", "row[6].name.text", name, "Kestrel Ridge Health");
                // Must reproduce the legacy behavior (gate baseline_failed, human instruction): an escaped
                // edit still counts as a pending change - row highlighted, changeCount 1, Save enabled.
                runner.AssertChanged("D2_Step1", "row[6].highlighted", e1HlBefore, hl.ToString().ToLower());
                runner.Assert("D2_Step1", "row[6].highlighted", hl,
                    "Row ID 6 is highlighted as changed after Escape", "Row ID 6 is NOT highlighted after Escape");
                runner.AssertChanged("D2_Step1", "changeCount", e1CountBefore, count);
                runner.AssertEquals("D2_Step1", "changeCount", count, "1");
                runner.AssertChanged("D2_Step1", "saveButton.enabled", e1SaveBefore, save);
                runner.AssertEquals("D2_Step1", "saveButton.enabled", save, "true");
            },
            gridSelector: "table.mud-table-root tbody");

        runner.Log("══════════════════════════════════════════════");
        runner.Log("TARGET MODULE D COMPLETE");
        runner.Log("══════════════════════════════════════════════");

        await runner.GenerateReport();

        if (runner.StepsFailed > 0 || runner.AssertionsFailed > 0)
            MarkIssueFound($"Module D target: {runner.StepsFailed} step(s), {runner.AssertionsFailed} assertion(s) failed vs reference baseline");
        else
            MarkVerified();
    }
}

// plan-revision: 1; steps=6
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;
using MigrationTests.Playwright;
using MigrationTests.Playwright.Helpers;

namespace MigrationTests.Playwright.Tests;

/// <summary>
/// REFERENCE MODULE D — PayorList: Edit a cell and Cancel (discard the batch). READ-ONLY.
///
/// User story (plan.json): "I start editing payors, change my mind and click Cancel, and
/// everything snaps back with nothing saved."
///
/// Reference system = ASP.NET WebForms + free-jqGrid at /Pages/PayorList.aspx (port 5100).
/// Cancel (#btnCancelAll) clears the client-side change set, reloads the grid from the service
/// and shows the #toast "Changes discarded.". Nothing is posted, so the module is read-only.
///
/// 3 main steps + sub-flow D1 (2 steps) + sub-flow D2 (1 step) = 6 steps (plan.json modules[D].steps).
/// CSS state (plan cssStates): after_cancel, right after the Step2 Cancel click.
/// </summary>
[TestFixture]
public class ReferencePayorListModuleD_v2 : TestBase
{
    private const string GridRowsSel = "#payorGrid tr.jqgrow";
    private const string ChangeCountSel = "#changeCount";
    private const string SaveBtnSel = "#btnSaveAll";
    private const string CancelBtnSel = "#btnCancelAll";
    private const string ToastSel = "#toast";
    private const string PagerInfoSel = "#payorPager .ui-paging-info";

    private const string EditedName3 = "Citrine EDITED";
    private const string EditedName4 = "Unity Crest EDITED";
    private const string EditedCode5 = "HUM999";
    private const string EscapedName6 = "Kestrel ESCAPED";

    #region Navigation

    private async Task<bool> NavigateToPage()
    {
        // Cold-started WebForms app compiles on first request: allow more than the 30s default.
        await Page.GotoAsync($"{Settings.ReferenceBaseUrl}/Pages/PayorList.aspx", new() { Timeout = 90000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 60000 });
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

    private async Task<string> GetDirtyMessage() => await Text(Page.Locator(ChangeCountSel));

    private async Task<string> GetChangeCount()
    {
        var m = Regex.Match(await GetDirtyMessage(), @"(\d+)\s+unsaved");
        return m.Success ? m.Groups[1].Value : "0";
    }

    private async Task<string> GetMessage() => await Text(Page.Locator(ToastSel));

    private async Task<string> IsEnabled(string sel) =>
        (await Page.Locator(sel).First.IsEnabledAsync()).ToString().ToLower();

    private async Task<string> GetSaveText() => await Text(Page.Locator(SaveBtnSel));

    private async Task<string> GetPagerInfo() => await Text(Page.Locator(PagerInfoSel));

    /// <summary>Unsaved-change highlight: jqGrid row carries class row-modified (markRowChanged).</summary>
    private async Task<bool> IsHighlighted(int id)
    {
        var row = Row(id);
        if (await row.CountAsync() == 0) return false;
        var cls = await row.First.GetAttributeAsync("class") ?? "";
        return cls.Split(' ').Contains("row-modified");
    }

    /// <summary>Inline Payor Name editor open in the row.</summary>
    private async Task<bool> IsNameEditorVisible(int id)
    {
        var input = Row(id).Locator("input.edit-name-input");
        return await input.CountAsync() > 0 && await input.First.IsVisibleAsync();
    }

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

    /// <summary>Click Cancel, wait for the toast and return its text; then let the grid reload settle.</summary>
    private async Task<string> CancelAll()
    {
        await Page.Locator(CancelBtnSel).ClickAsync();
        await Page.Locator($"{ToastSel}.toast-info")
            .First.WaitForAsync(new() { Timeout = 10000, State = WaitForSelectorState.Visible });
        var msg = await GetMessage();
        await Page.WaitForTimeoutAsync(2000); // reloadGrid settle
        return msg;
    }

    /// <summary>Open the Payor Name editor, type a value and press Escape (no blur call).</summary>
    private async Task EditNameAndEscape(int id, string value)
    {
        await Row(id).Locator("span.cell-name").First.ClickAsync();
        var input = Row(id).Locator("input.edit-name-input").First;
        await input.WaitForAsync(new() { Timeout = 5000 });
        await input.FillAsync(value);
        await input.PressAsync("Escape");
        await Page.WaitForTimeoutAsync(500);
    }

    #endregion

    [Test]
    public async Task ReferencePayorList_ModD()
    {
        var runner = new ModuleTestRunner("PayorList", "D", "Reference", 0);

        runner.Log("══════════════════════════════════════════════");
        runner.Log("REFERENCE MODULE D — PayorList: Edit + Cancel");
        runner.Log("══════════════════════════════════════════════");

        if (!await NavigateToPage())
        {
            runner.StepFail("Step1", "ABORTED: navigation to reference PayorList failed (grid rows never rendered)");
            await runner.GenerateReport();
            MarkIssueFound("Navigation failed — reference grid did not load");
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
            action: async () => await EditText(3, "cell-name", "edit-name-input", EditedName3),
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
            gridSelector: "#payorGrid tbody");

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
            action: async () => { s2Msg = await CancelAll(); },
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
            gridSelector: "#payorGrid tbody");

        try { await CssCompareHelper.DiscoverAndCaptureAll(Page, "reference", "after_cancel"); } catch { }

        // ════════════════════════════════════════════════════════════════
        // STEP 3 — Re-read ID 3 after a fresh page load (NOT persisted)
        // ════════════════════════════════════════════════════════════════
        var s3NameBefore = await GetName(3);
        var s3CountBefore = await GetChangeCount();
        await runner.ExecuteStep("Step3", Page, "Re-read the ID 3 row (verify NOT persisted)",
            action: async () => { await NavigateToPage(); },
            assertions: async () =>
            {
                var name = await GetName(3);
                runner.Capture("S3_reload", "row[3].name.text", name);
                runner.AssertUnchanged("Step3", "row[3].name.text", s3NameBefore, name);
                runner.AssertUnchanged("Step3", "changeCount", s3CountBefore, await GetChangeCount());
            },
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW D1 STEP 1 — Edit Name of ID 4 and Code of ID 5
        // ════════════════════════════════════════════════════════════════
        var d1CountBefore = await GetChangeCount();
        var d1MsgBefore = await GetDirtyMessage();
        var d1Name4Before = await GetName(4);
        var d1Code5Before = await GetCode(5);
        var d1SaveTextBefore = await GetSaveText();
        await runner.ExecuteStep("D1_Step1", Page, $"Edit the Name of ID 4 ('{EditedName4}') and the Code of ID 5 ('{EditedCode5}')",
            action: async () =>
            {
                await EditText(4, "cell-name", "edit-name-input", EditedName4);
                await EditText(5, "cell-code", "edit-code-input", EditedCode5);
            },
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW D1 STEP 2 — Cancel
        // ════════════════════════════════════════════════════════════════
        var d2MsgBefore = await GetMessage();
        var d2NameBefore = await GetName(4);
        var d2CodeBefore = await GetCode(5);
        var d2CountBefore = await GetChangeCount();
        string d2Msg = "";
        await runner.ExecuteStep("D1_Step2", Page, "Click Cancel",
            action: async () => { d2Msg = await CancelAll(); },
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
            gridSelector: "#payorGrid tbody");

        // ════════════════════════════════════════════════════════════════
        // SUB-FLOW D2 STEP 1 — Name of ID 6: type a value, press Escape
        // ════════════════════════════════════════════════════════════════
        var e1NameBefore = await GetName(6);
        var e1HlBefore = (await IsHighlighted(6)).ToString().ToLower();
        var e1CountBefore = await GetChangeCount();
        var e1SaveBefore = await IsEnabled(SaveBtnSel);
        await runner.ExecuteStep("D2_Step1", Page, $"Click the Payor Name of ID 6, type '{EscapedName6}' and press Escape",
            action: async () => await EditNameAndEscape(6, EscapedName6),
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
                // Legacy behavior (gate baseline_failed, human instruction): an escaped edit still
                // counts as a pending change - row highlighted, changeCount 1, Save enabled.
                runner.AssertChanged("D2_Step1", "row[6].highlighted", e1HlBefore, hl.ToString().ToLower());
                runner.Assert("D2_Step1", "row[6].highlighted", hl,
                    "Row ID 6 is highlighted as changed after Escape", "Row ID 6 is NOT highlighted after Escape");
                runner.AssertChanged("D2_Step1", "changeCount", e1CountBefore, count);
                runner.AssertEquals("D2_Step1", "changeCount", count, "1");
                runner.AssertChanged("D2_Step1", "saveButton.enabled", e1SaveBefore, save);
                runner.AssertEquals("D2_Step1", "saveButton.enabled", save, "true");
            },
            gridSelector: "#payorGrid tbody");

        runner.Log("══════════════════════════════════════════════");
        runner.Log("REFERENCE MODULE D COMPLETE");
        runner.Log("══════════════════════════════════════════════");

        await runner.GenerateReport();

        if (runner.StepsFailed > 0 || runner.AssertionsFailed > 0)
            MarkIssueFound($"Module D reference: {runner.StepsFailed} step(s), {runner.AssertionsFailed} assertion(s) failed");
        else
            MarkVerified();
    }
}

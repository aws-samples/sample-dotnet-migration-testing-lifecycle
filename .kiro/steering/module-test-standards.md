---
inclusion: manual
---

# Module Test Design Standards

Reference: `#module-test-standards.md`

Mandatory standards for designing, implementing, and reporting modular Playwright test cases.

---

## 1. Module Structure

### File Organization

**File location rule:** each module is its own NUnit project under
`.kiro/test-project/Modules/{PageName}.Mod{X}/`, holding exactly the Reference and the Target
test of that module. The project file `{PageName}.Mod{X}.csproj` is created by
`lifecycle.ps1 -Action build-module -Page {PageName} -Module {X}` when missing and references
the shared `Harness/` project. Never put test files anywhere else, and never edit
`Harness/**` or `Reports/**` from a test step.

```
.kiro/test-project/Modules/
├── {PageName}.ModA/
│   ├── {PageName}.ModA.csproj           (created by build-module)
│   ├── Reference{PageName}ModuleA_v2.cs (Reference, test method Reference{PageName}_ModA)
│   └── {PageName}ModuleA_v2.cs          (Target, test method {PageName}_ModA)
├── {PageName}.ModB/
│   └── ...
└── ...
```

Line 1 of every test file is `// plan-revision: <n>; steps=<k>` (`<n>` = plan.json `revision`,
`<k>` = the module's `plannedSteps`). Build only with `build-module`
(it builds the Harness once and then just this module, so parallel authors never collide).

### Class Structure
```csharp
[TestFixture]
public class {PageName}Module{X} : TestBase
{
    private readonly List<TestBehaviorResult> _results = new();
    // Single [Test] method — all behaviors in one page context
    // CSS capture at end
    // Module report output
}
```

### Single Page Context Rule
- Each module = ONE `[Test]` method
- ONE navigation call (shared helper)
- All behaviors execute sequentially within the same page
- No state leaks between modules (each gets fresh browser page)

---

## 2. Test Behavior Pattern (MANDATORY)

```csharp
// ─── ID-XX: [Description] ───
// Step 1: SETUP — prepare the state
// Step 2: ACTION — perform the interaction
// Step 3: VERIFY — check outcome with evidence
// Step 4: CLEANUP — restore state (if needed)
```

### CORRECT (functional verification):
```csharp
// SETUP: Record initial state
var initialRowCount = await gridRows.CountAsync();
var initialFirstRow = await gridRows.First.Locator("td").Nth(1).TextContentAsync();

// ACTION: Apply filter
await filterDropdown.SelectOptionAsync("value");
await searchButton.ClickAsync();
await Page.WaitForTimeoutAsync(3000);

// VERIFY: Grid content changed
var filteredCount = await gridRows.CountAsync();
var filteredFirstRow = await gridRows.First.Locator("td").Nth(1).TextContentAsync();
if (filteredCount != initialRowCount || filteredFirstRow != initialFirstRow)
    Verified("ID-XX", "Filter changes results",
        $"Before: {initialRowCount} rows, After: {filteredCount}. First row: '{initialFirstRow}' → '{filteredFirstRow}'");

// CLEANUP
await clearButton.ClickAsync();
await Page.WaitForTimeoutAsync(3000);
```

### WRONG (presence-only — NOT acceptable):
```csharp
// ❌ Only checks element exists, doesn't verify FUNCTION
if (await element.IsVisibleAsync())
    Verified("ID-XX", "Element visible", "found");
```

---

## 3. Result Tracking

```csharp
private void Verified(string id, string desc, string evidence) { ... }
private void Finding(string id, string desc, string details) { ... }
private void NotTested(string id, string desc, string reason) { ... }
```

---

## 4. Logging Standards

Every step logs:
- Action taken
- Before state values
- After state values
- PASS/FAIL verdict with evidence
- Timing information

---

## 5. CSS Capture

Capture at defined visual states using `CssCompareHelper.DiscoverAndCaptureAll()`.
States per module defined during Phase 0 analysis.

---

## 6. Module Report Format

```
══════════════════════════════════════════════
MODULE [X] COMPLETE — [Story Name]
══════════════════════════════════════════════

Summary: Verified: X | Findings: Y | Not Tested: Z | Total: N

| # | ID | Description | Status | Evidence |
|---|-----|-------------|--------|----------|
| 1 | XX  | description | VERIFIED | data... |

CSS States Captured: [list] ✓
```

---

## 7. Acceptable NOT_TESTED Reasons

Valid:
- Multi-user scenario (requires different credentials)
- Destructive data (would permanently delete test data)
- Browser limitation (HTML5 rejects at input level)
- External dependency (unavailable in test environment)

NOT valid:
- "Couldn't find selector" → fix it
- "Timeout" → fix timing
- "Service down" → restart and retry
- "No data" → use correct test data

---

## 8. Quality Gate — Module Done When:

- [ ] Every behavior has INTERACTION + OUTCOME verification
- [ ] All Findings fixed or escalated with evidence
- [ ] CSS captured for all defined states
- [ ] Navigation log saved
- [ ] Report matches mandatory format
- [ ] No presence-only verifications remain


---

## 9. Page State Snapshot — Mandatory Verification Pattern

### Core Principle

Testing is state-machine verification:
1. Analyze source code → identify ALL states
2. Identify ALL transitions (user actions between states)
3. For each transition — document what SHOULD change and what SHOULD NOT
4. Capture snapshots at each state (data + HTML + CSS)
5. Test cases ARE the assertions derived from transitions
6. Run on both systems — Reference captures = truth, Target must match

### Generic State Model

```csharp
private record PageState
{
    // Info section
    public string InfoField1 { get; init; } = "";
    public string InfoField2 { get; init; } = "";

    // Controls
    public string SaveButtonState { get; init; } = "";  // "clean" or "dirty"
    public bool SaveButtonVisible { get; init; }
    public bool CancelButtonVisible { get; init; }

    // Grid
    public int GridRowCount { get; init; }
    public List<RowSnapshot> GridRows { get; init; } = new();

    // Messages
    public bool MessageVisible { get; init; }
    public string MessageText { get; init; } = "";

    // Dialog (transient)
    public bool DialogVisible { get; init; }
    public string DialogFieldValue { get; init; } = "";

    // Pagination
    public string PagerInfoText { get; init; } = "";
}

private record RowSnapshot
{
    public int Index { get; init; }
    public string PrimaryText { get; init; } = "";
    public Dictionary<string, string> FieldValues { get; init; } = new();
    public Dictionary<string, bool> CheckboxStates { get; init; } = new();
    public Dictionary<string, string> DropdownValues { get; init; } = new();
    public string RowBgColor { get; init; } = "";
}
```

### Safe Capture Helpers

```csharp
private async Task<string> SafeText(string selector)
{
    var el = Page.Locator(selector).First;
    return await el.CountAsync() > 0 ? (await el.TextContentAsync())?.Trim() ?? "" : "";
}

private async Task<string> SafeAttr(string selector, string attr)
{
    var el = Page.Locator(selector).First;
    return await el.CountAsync() > 0 ? await el.GetAttributeAsync(attr) ?? "" : "";
}

private async Task<bool> SafeChecked(ILocator el)
{
    try { return await el.IsCheckedAsync(); } catch { return false; }
}

private async Task<bool> IsPresent(string selector)
{
    return await Page.Locator(selector).CountAsync() > 0;
}
```

### State Comparison Pattern

```csharp
private string CompareStates(PageState before, PageState after, string actionName)
{
    var diffs = new List<string>();
    if (before.GridRowCount != after.GridRowCount)
        diffs.Add($"RowCount: {before.GridRowCount} → {after.GridRowCount}");
    if (before.SaveButtonState != after.SaveButtonState)
        diffs.Add($"Save: '{before.SaveButtonState}' → '{after.SaveButtonState}'");
    if (before.MessageText != after.MessageText)
        diffs.Add($"Message: '{before.MessageText}' → '{after.MessageText}'");
    // Per-row comparison...
    return diffs.Count > 0
        ? $"After '{actionName}': {string.Join("; ", diffs)}"
        : $"After '{actionName}': NO CHANGES";
}
```

### When to Capture WHAT

| Action | Before | After | Assert Changed | Assert Unchanged |
|--------|--------|-------|---------------|------------------|
| Sort | All row values (ordered) | Same fields | Row order different | Count same, buttons same |
| Dropdown change | Value, save state | Same | New value, save=dirty | Count same |
| Save (OK) | Rows, controls, dialog | Same | Dialog closed, save=clean, rows updated | Info section |
| Cancel | Full state | Full state | NOTHING differs from pre-dirty | Everything reverted |
| Upload/Add | Row count, list | Same + new | Count+1, new item present | Existing unchanged |
| Delete | Row count, item | Same - item | Count-1, item gone | Other items unchanged |

---

## 10. Failure Escalation Report Format

When escalating to human:

```
══════════════════════════════════════════════
FAILURE REPORT — Module [X], Step [N]
══════════════════════════════════════════════

Test Module: [ClassName]
Failure Point: [Step / behavior ID]
Duration: [Xs]

Error Details:
- Error: [exact message]
- Selector: [what was being looked for]
- URL: [page URL at failure]
- Service Status: [UP/DOWN]

Diagnostic Evidence:
- HTML Dump: [file path or inline]
- Grid State: [rows visible, content]

Root Cause Analysis:
- [What was tried]
- [What data shows]
- [What human input is needed]
```


---

## 11. CSS Capture for Transient Elements

Transient UI elements (dialogs, popups, tooltips, expanded dropdowns) require special capture timing. Getting this wrong produces empty/missing CSS data.

### Core Rule

> **Capture CSS WHILE the transient element is VISIBLE, BEFORE dismiss/close.**

### Timing Diagram

```
CORRECT:
  OpenDialog() → WaitForVisible() → CaptureCss("dialog_open") → CloseDialog()
                                      ↑ CAPTURE HERE

WRONG:
  OpenDialog() → CloseDialog() → CaptureCss("dialog_open")
                                  ↑ TOO LATE — elements gone, empty results
```

### Elements to Capture in Dialog State

When a dialog/modal is open, capture ALL of these elements:

| Element | Selector Pattern | Properties to Capture |
|---------|-----------------|----------------------|
| Dialog container | `.dialog-overlay`, `.mud-dialog` | width, height, background-color, border, border-radius, box-shadow |
| Dialog title | `.dialog-title`, `.mud-dialog-title` | font-family, font-size, font-weight, color |
| Input fields (text) | `dialog input[type=text]`, `dialog textarea` | font-family, font-size, color, border, padding, background-color |
| Buttons (OK/Cancel) | `dialog button`, `.dialog-actions button` | font-family, font-size, font-weight, color, background-color, border, padding |
| Overlay/backdrop | `.mud-overlay`, `.dialog-backdrop` | background-color, opacity |
| Labels | `dialog label` | font-family, font-size, font-weight, color |
| Dropdowns | `dialog select` | font-family, font-size, border, padding |

### CSS Properties — Standard Capture Set

For EVERY element captured (transient or persistent):

```
font-family, font-size, font-weight, font-style,
color, background-color,
border, border-color, border-width, border-radius,
padding, margin,
text-align, text-decoration,
display, visibility, opacity,
width, height, min-height, max-height
```

### State Naming Convention

```
{page}_{element}_{state}
```

Examples:
- `entitylist_dialog_open` — dialog visible with form fields
- `entitylist_grid_dirty` — grid after user made changes
- `entitylist_page_empty` — page with no records
- `entitylist_dropdown_expanded` — multi-select open showing options

### Common Mistakes

| # | ❌ WRONG | ✅ RIGHT | Why |
|---|---------|---------|-----|
| 1 | Capture after `dialog.CloseAsync()` | Capture after `WaitForDialogOpen()`, THEN close | Elements gone after close = empty CSS |
| 2 | Capture dropdown CSS when collapsed | Open dropdown, capture, then collapse | Expanded state shows option styling |
| 3 | Capture tooltip after mouse leaves | Hover → wait → capture → move mouse | Tooltip disappears on mouse leave |
| 4 | Single capture for all dialog states | Separate captures: empty form vs filled form | Different states may have different styling (validation highlights) |

### Implementation Pattern

```csharp
// ─── CSS Capture: Dialog Open State ───
await OpenDialog();
await Page.WaitForSelectorAsync(".dialog-container", new() { State = WaitForSelectorState.Visible });
await Page.WaitForTimeoutAsync(500); // Let animations complete

// Capture NOW — dialog is visible
await CssCompareHelper.DiscoverAndCaptureAll(Page, "dialog_open", outputPath);

// THEN close
await CloseDialog();
await Page.WaitForSelectorAsync(".dialog-container", new() { State = WaitForSelectorState.Hidden });
```

### Validation Rule

After CSS capture for a transient state, verify the output file contains the expected elements:
- If dialog capture has 0 button entries → capture failed (dialog was already closed)
- If dropdown capture has 0 option entries → capture failed (dropdown was collapsed)
- Re-run with corrected timing if validation fails

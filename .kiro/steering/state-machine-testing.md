---
inclusion: manual
---

# State-Machine Testing Framework

Reference: `#state-machine-testing.md`

Every web page is a state machine. Testing = verifying that state transitions produce exactly the expected changes and nothing else.

---

## Core Principle

```
THE TEST IS NOT "click sort header"
THE TEST IS "at State S0, perform sort → arrive at State S7
  where rows are in different order, and EVERYTHING ELSE is
  unchanged (row count, controls, buttons, messages, URL)"
```

---

## Design Process

### Step 1: Analyze Source Code → Extract States & Transitions

| Source | What to Extract |
|--------|----------------|
| Markup | Controls, conditional rendering, event handlers, grid columns |
| Code-behind | Page load, button handlers, grid events, data binding, redirects |
| Client-side | onchange/onclick, DOM manipulation, dialogs, Ajax, dirty tracking |

### Step 2: Define States

| Field | Description |
|-------|-------------|
| State ID | S0, S1, S2... |
| Name | Descriptive (e.g., "Clean", "Dirty", "Dialog Open") |
| How to Reach | What action puts you here |
| Key Indicators | Discriminators (save.src, dialog.visible, etc.) |

### Step 3: Design Modules as Workflows

**A module = one complete user workflow performed end-to-end.**

Principles:
1. Each module is a complete workflow (start to finish)
2. Steps are sequential (step N depends on step N-1)
3. Sub-flows handle edge cases/validations
4. Dirty + Save is one module. Dirty + Cancel is a different module.
5. Sorting belongs with read-only actions
6. NOT TESTED items: mark clearly, never skip silently

### Step 4: Derive Assertions Per Step

Each step produces:
- **CHANGES** = fields that MUST differ (proves action worked)
- **STAYS SAME** = fields that MUST NOT change (regression guards)

---

## Three Dimensions of Verification

| Dimension | What | Method | Cross-System Use |
|-----------|------|--------|-----------------|
| Data | Values, counts, selections | TextContent, InputValue, IsChecked | Primary comparison |
| HTML | Full DOM dump | outerHTML | Within-system safety net |
| CSS | Computed styles | getComputedStyle | Styling comparison |

### Cross-System Rules

| Comparison | Dimension | Purpose |
|-----------|-----------|---------|
| Reference.state vs Target.state | DATA only | Functional parity |
| Reference CSS vs Target CSS | CSS only | Visual parity |
| Before.html vs After.html (same system) | HTML | Safety net |

---

## Capture Strategy

- **Capture inline** with each action (DOM exists only at that moment)
- **Always fresh** — never reuse a capture from earlier
- At every state: HTML dump + JSON state + CSS styles

### Naming Convention
```
{System}_{ModuleId}_{Seq:000}_{StateId}_{StateName}.{ext}

Examples:
Reference_ModA_001_S0_clean.json
Target_ModA_002_S1_dirty.json
Target_ModA_003_UNEXPECTED_no_dialog.html
```

---

## State Gate

After every action, verify you reached the expected state:
```csharp
var actualState = await IdentifyCurrentState();
if (actualState != ExpectedTarget)
{
    await CaptureHtmlDump("UNEXPECTED");
    Finding(id, $"Expected {Expected}, got {actualState}");
    return; // Do NOT proceed
}
```

---

## HTML Diff Safety Net

After assertions, diff before.html vs after.html:
- Differences covered by assertions → OK
- In ignore list (framework noise) → skip
- UNCOVERED → flag as:
  - Exists in reference too → MISSING TEST CASE
  - Only in target → potential APP BUG

---

## Report Formats

### Module Report (Per Phase)

```markdown
# Module [X] Report — [Story]

> System: [Reference | Target]
> Steps: N | Sub-flows: N
> Result: [PASS | FAIL] (N/N assertions passed)

## Main Flow Results
### Step 1: [Action] — [PASS|FAIL]
| # | Field | Before | After | Expected | Result |

## Findings
[Plain English description of what went wrong]

## Verdict: [✓ PASS | ✗ FAIL — N findings]
```

### Parity Report (Phase 3)

```markdown
# Module [X] — Data Parity (Reference vs Target)

## Per-State Comparison
| # | Field | Reference | Target | Match? |

## Verdict: [✓ PASS | ✗ FAIL]
```

### CSS Diff Report

```markdown
# Module [X] — CSS Diff

| Element | Property | Reference | Target | Severity |

## Action Items
| # | Element | Fix | Target File |
```

---

## Key Principles

1. Reference is ALWAYS first
2. Modules are complete workflows (not grouped by action type)
3. Steps are sequential within a module
4. HTML diff is the safety net
5. No uncovered diffs allowed
6. Sequential, not parallel
7. State gate prevents cascading failures
8. Every state gets fresh capture
9. Iterate until clean
10. NOT TESTED ≠ skipped

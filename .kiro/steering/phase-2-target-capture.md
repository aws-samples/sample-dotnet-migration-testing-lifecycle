---
inclusion: manual
---

# Phase 2: Target System Capture & Validate — Execution Steps

Reference: `#phase-2-target-capture.md`
Pre-requisite: Plan signed off (gate 1). Target tests are written together with the
Reference tests in the parallel `author` node (one slot per module).

**No human gate in this phase.** For each module the runner starts Target together with
Reference in the same `run-page` call (the Target verdict needs a valid Reference baseline).

---

## Purpose

Run the SAME workflow tests against the target system. Assertions use EXACT values from the
Reference. Any difference = FINDING (potential bug in the target system).

---

## Per Module (Target runs right after Reference)

### Step 2.1: Write Target Test File

File naming: `.kiro/test-project/Modules/{PageName}.Mod{X}/{PageName}Module{X}_v2.cs` (no "Reference" prefix), test method
`{PageName}_Mod{X}` (or `plan.json targetTest`), line 1 `// plan-revision: <n>; steps=<k>`.

Requirements:
- Same steps as the Reference test (same workflow, same captures, same CSS states)
- Navigation uses the target system's routing/patterns
- State gate after each action (unexpected state = immediate bug finding)

**Mirror-every-assertion rule:**
- Target step names are identical to the Reference step names.
- Every Reference assertion is mirrored on Target with the same `(step, field, type)` and in
  the same order.
- Every Reference `Capture(state, field)` is mirrored on Target in the same step, with the
  same `state` and `field`; the value is the Target's runtime value.

**Verbatim-constant rule:**
- Only **EQUALS/CONTAINS** expected constants are copied verbatim from the Reference
  assertion with the same `(step, field, type)`.
- CHANGES and STAYS SAME are mirrored as assertions whose "before" comes from Target runtime
  state (a value the Target test read earlier), never as a copied constant. BOOL has no
  constant to copy.
- Never "non-empty" or CONTAINS where the Reference has EQUALS.

The report generator enforces both rules: an unmirrored Reference assertion or capture is a
counted `test-coverage` finding (or `missingAssertions` / `missingCaptures` on another
finding), and a changed constant is a counted `test-expectation` finding.

**Target navigation may differ** (e.g. SPA routing vs postbacks). The WHAT stays the same;
only HOW to navigate/locate elements differs. Use selectors from
`.kiro/steering/selector-registry.md`.

### Step 2.2: QA Review (self-review, no gate)

- Every Reference assertion has a Target partner with the same `(step, field, type)`
- EQUALS/CONTAINS constants are identical to the Reference ones
- Same step count and sub-flows

### Step 2.3: (no gate) Build

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action build-module -Page <PageName> -Module <X>
```

Do not ask for approval to run the Target test.

### Step 2.4: Run (via the runner)

The Target test runs inside
`.\.kiro\test-project\lifecycle.ps1 -Action run-page -Page <PageName> -Modules A,B,C,D`,
alongside the Reference test of the same module. After a fix, rerun only Target with
`.\.kiro\test-project\lifecycle.ps1 -Action rerun-target -Page <PageName> -Modules <X,Y>`.
The verdict comes from `runs/Module<X>/Target/results.json`, never from the test process
exit code.

### Step 2.5: Analyze Results

For each step:
- PASS (matches Reference) → parity confirmed
- FAIL:
  - **State gate unexpected** → APP BUG (`app` finding)
  - **Value mismatch** → FINDING (Reference vs actual)
  - **Selector not found / timeout** → `test-selector` finding, auto-fixed in Phase 4
  - **Environment** → the runner returns exit 3 (it already did preflight)

---

## Target Module Report — 5 Sections (written by the runner)

Presenting is satisfied by `reports/Module<X>_Parity.md`, `_CSS_Diff.md`, `_Performance.md`
and `_ManualTests.md`; they are shown at the final gate. Never pause to present.

**Section 1: Step Report**
```markdown
| Step | Status | Description (plain English with data values) |
```

**Section 2: Performance Comparison**
```markdown
| Step | Action | Reference (ms) | Target (ms) | Difference |
```

**Section 3: Functional Gaps**
```markdown
| # | Step | Field | Reference Behavior | Target Behavior | Severity |
```

**Section 4: CSS Differences**
```markdown
| Control | Property | Reference Value | Target Value | Severity | Real/False Positive |
```

**Section 5: Summary**
```markdown
| Metric | Value |
|--------|-------|
| Assertions | N/N passed |
| Functional Gaps | N |
| CSS Differences | N (N actionable, N accepted/LOW) |
```

---

## CSS Capture (MANDATORY)

At every planned CSS state, `CssCompareHelper.DiscoverAndCaptureAll()` runs on BOTH systems.
The generator compares the elements mapped in `state-map/css-map.json` per state, per
module, and counts HIGH/MEDIUM diffs that are not `accepted` as `actionableCssDiffs`. A
planned state missing on Target is also actionable.

| Category | Criteria | Action |
|----------|----------|--------|
| 🔴 Real (Must Fix) | font-weight, color, visibility differences | Auto-fix in Phase 4 |
| 🟡 Structural | height/width >5px, display type differences | Auto-fix or `accepted` with reason in css-map.json |
| 🟢 Cosmetic | ±1px, font-family fallbacks | LOW, not actionable |

---

## Key Rules

1. **Wait time parity** — Target tests MUST use the same wait times as Reference
2. **Sort validation** — Test BOTH ASC and DESC before declaring sort broken
3. **FAIL is honest** — Never mark PASS when expected behavior didn't occur
4. **Data assertions required** — Every action must capture before/after data
5. **Never weaken assertions** — a mismatch is a finding, not a test to loosen

---

## Runner-Verified Checks (no gate)

| # | Check | Evidence |
|---|-------|----------|
| 1 | Target test file builds | `[build] Module X PASS <ms>ms log=...` (run-page) or `[build-module] X PASS` |
| 2 | Target executed | `status.json modules.X.target.status != "NOT_RUN"` |
| 3 | Per-step comparison produced | `reports/ModuleX_Parity.md` |
| 4 | CSS captured for all planned states | `reports/ModuleX_CSS_Diff.md`, `actionableCssDiffs` |
| 5 | Per-step timings for both systems | `timings.json latest.X.Target.steps` |

---

## Failure Analysis (in the module report)

Failed Target steps are listed with a fix type; Phase 4 acts on them without asking.

```markdown
## Failure Analysis — Module [X]

| # | Failed Step | Assertion | Expected | Actual | Root Cause | Fix Type |
|---|-------------|-----------|----------|--------|-----------|----------|
| 1 | Step3 | checkbox toggled | True | False | Selector wrong (.mud-checkbox vs .checkbox) | TEST FIX (auto) |
| 2 | E1 | row count | 8 | 9 | Cancel doesn't reload grid from server | APP BUG (auto-fix) |
| 3 | Step2 | dialog visible | True | False | Timing — dialog opens slower in target | TEST FIX (auto) |
```

### Fix Type Definitions

| Fix Type | Meaning | Action |
|----------|---------|--------|
| **TEST FIX (auto)** | Target test issue (selector, wait, capture order, missing mirrored assertion) | Fixed in Phase 4, max 3 attempts |
| **APP BUG (auto-fix)** | Target app behaves differently from Reference | Fixed in the Target source (status.json `paths.target`, appsettings `TargetSourcePath`) in Phase 4 (max 2 rounds); UNRESOLVED in the final report if still open |
| **ENV ISSUE** | Service down, data missing | Runner exit 3; rerun once, then report the doctor fix |
| **ACCEPTABLE DIFF** | Intentional target difference | Proposed in the final report; the human decides at gate 2 |

### Post-Fix Verification

After fixes, the runner re-runs the full Target module (`rerun-target`), keeps the previous
result as `results.prev.json`, and records before/after counts in `fixLoop.history`.
Previously passing steps must still pass (no regressions).

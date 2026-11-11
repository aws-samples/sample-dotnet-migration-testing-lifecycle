---
inclusion: manual
---

# Phase 1: Reference System Baseline — Execution Steps

Reference: `#phase-1-reference-baseline.md`
Pre-requisite: Plan signed off (gate 1). Preflight is done by the runner (`#preflight.md`).
For test code standards: `#module-test-standards.md`

**No human gate in this phase.** Tests are written in the parallel `author` node (slots
`author-1..6`, one module per slot) and run in the `run` step by `lifecycle.ps1 run-page`. The human reviews the baseline values in the final
report (gate 2), side by side with the Target values. The only stop is the
**Reference-baseline exception gate** (Step 1.5).

---

## Purpose

Establish the source-of-truth baseline by running tests against the reference system. Every field value captured here becomes the expected value for Phase 2 comparison.

---

## Execute Per Module (A, B, C... each in its own author slot, in parallel)

### Step 1.1: Write Reference Test File

File naming: `.kiro/test-project/Modules/{PageName}.Mod{X}/Reference{PageName}Module{X}_v2.cs`
(each module is its own project, `{PageName}.Mod{X}.csproj`, created by `build-module`).
Step names are the plan.json `steps[].id` values.

Requirements:
- One `[Test]` method per module
- Instantiate `ModuleTestRunner` at top
- Own navigation method (fresh page context)
- Uses test data from `appsettings.test.json`
- For each action step:
  1. Capture BEFORE state (HTML + field values)
  2. Perform action
  3. Capture AFTER state
  4. `runner.AssertChanged()` for fields that SHOULD differ
  5. `runner.AssertUnchanged()` for fields that MUST NOT differ
  6. `runner.CompareHtml()` for HTML diff safety net
  7. `runner.StepPass()` with plain English summary
- End: `await runner.GenerateReport()`

**Assertion minimums per step type:**
- Page load: ~15-20 (every visible element + value)
- State-changing action: ~5-8 (what changed + what stayed same)
- No-op action: ~4-5 (everything unchanged)

### Step 1.2: QA Review (Self-Review)

Verify:
- Every step from `transitions.md` has a test section
- Every assertion from `assertions.md` is implemented
- CSS capture at every planned state
- No hardcoded test data in code body

### Step 1.3: (no gate) Continue to build

The plan sign-off already covers the test design. Do not ask "ready to build and run?".

### Step 1.4: Build

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action build-module -Page <PageName> -Module <X>
```
It builds the shared Harness once (mutex-protected) and then only this module, and prints
`[build-module] X PASS 3.1s (Ref 13 steps/54 assertion calls, Tgt 13/38, rev 1)`. If it
fails: read the `error CS` lines in `logs/build-<X>.log`, fix, rebuild. Max 3 attempts per
distinct error. Never call `dotnet` directly.

### Step 1.5: Run Reference (via the runner)

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action run-page -Page <PageName> -Modules A,B,C,D
```

One command for all modules (the workflow adds `-Detach` and relays results with
`-Action wait`). The runner runs all `read-only` modules in parallel, Reference and Target of
a module together, then each `mutating` module alone with a service restart after each system.
It captures CSS, writes the four module reports, renders the `module-result` chat view and
updates `status.json`, `timings.json` and `PROGRESS.md` after every step (20-minute hard
timeout per test process).

**Baseline failed = exit 2.** Reference results.json missing or aborted, any failed step or
assertion, or a planned CSS state not captured.

- **Auto-fix (Reference selectors/waits only):** if results.json shows `abortReason`
  timeout or a `__missing`/empty capture, fix only selectors and waits in
  `Modules/{PageName}.Mod{X}/Reference{PageName}Module{X}_v2.cs`, `build-module` and re-run
  that module, up to 3 attempts. Never
  change a Reference expected constant on your own.
- **Exception gate:** if it still fails, or the failure is an assertion value:
  `.\.kiro\test-project\lifecycle.ps1 -Action gate -Page <PageName> -Gate baseline_failed -State pending -ReportPath reports/Module<X>_Parity.md`,
  present the evidence (step, field, expected vs actual) and wait. Write the reply to
  `gate-response.txt` and run `... -Gate baseline_failed -State confirmed -ResponseFile .kiro/app-testing/<PageName>/gate-response.txt`.
  Then: "retry" re-runs the module; "correct the expected value to X" is applied to the
  Reference test, rebuilt and re-run; "stop" ends the run.

### Step 1.6: Report (written, not presented)

The runner writes the 3 Reference sections into `reports/Module<X>_Parity.md`; presenting is
satisfied by that file and it is shown at the final gate. Never pause to present. The
sections are:

**Section 1: Step Report**
```markdown
| Step | Status | Description |
|------|--------|-------------|
| Step1 | ✅ PASS | [Plain English with actual data values] |
```

**Section 2: HTML Diff Report**
```markdown
| Step | Uncovered Diffs | Notes |
|------|-----------------|-------|
| Step1 | 0 | — |
```

**Section 3: Summary**
```markdown
| Metric | Value |
|--------|-------|
| Assertions | N total / N passed / N failed |
| Steps | N PASS / N FAIL / N NOT TESTED |
| Baseline Values | [table of key captured values] |
```

Continue with Target (same `run-page` call) and the next module without waiting.

### Step 1.7-1.9: HTML Diff Safety Net

- Compare before/after HTML for each step
- DOM changes not covered by assertions = MISSING TEST CASE
- Add assertion, rerun until zero uncovered diffs

### Step 1.10: Baseline review (moved to the final report)

There is no per-module baseline confirmation. The final report (gate 2) shows every
Reference baseline value side by side with the Target value; the human confirms them there.

---

## Report Presentation Rules

### Step Descriptions MUST:
- Be plain English sentences
- Include actual data values (e.g., "first row changed from 'X' to 'Y'")
- State what stayed the same (no side effects)
- Mention UI feedback (messages, indicators)

**BAD:** `sortArrow.date 'none'→'▲', grid.row[0] changed`
**GOOD:** `Sorted by Date: first row changed from 'TestUpload' to 'Southern Health', arrow ▲ appeared, row count unchanged at 8, Save stayed clean`

---

## PASS/FAIL Rules (Non-Negotiable)

1. NEVER mark PASS unless ALL assertions passed with verifiable data
2. NEVER mark PASS if expected change did NOT occur — mark FAIL with reason
3. NEVER overwrite baseline assertions — they define expected behavior
4. NEVER weaken an assertion to make it pass
5. If fails: investigate root cause (selector? timing? app bug?) — do NOT mark PASS with a note

---

## After Each Module

The runner records the result in `status.json` (`modules.X.reference` counts, `executedAt`)
and `PROGRESS.md` (module table). Nothing to update by hand and no approval to ask for.

---

## New Learnings Section

### Sort Assertion Strategy

Compare the FULL column sequence before/after. Never rely solely on one row of a different column (data coincidence can cause false failures).

### Pagination — All 6 Scenarios Mandatory

Every paginated grid MUST test: Next, Previous, Last, First, Page Size Change, Direct Page Input.

### Column Index Discovery

At page load, capture header text → index mapping. Don't hardcode indices (hidden columns may shift them).


---

## What is NOT Acceptable (Report Quality Gate)

The following report outputs are REJECTED. If your output matches any of these patterns, redo the report before presenting to the human.

| # | Bad Pattern | Why It's Rejected | What's Required Instead |
|---|-------------|-------------------|------------------------|
| ❌ | "Module X passed with 20/20 assertions" | Too brief — no data shown | Full step report with every assertion's actual values |
| ❌ | Only summary section without Step Report + HTML Diff | Missing mandatory sections | ALL 3 sections presented (Step Report, HTML Diff, Summary) |
| ❌ | Raw attribute names: `sortArrow.date 'none'→'▲'` | Not human-readable | Plain English: "Sorted by Date: arrow ▲ appeared, first row changed from 'X' to 'Y'" |
| ❌ | A module report missing any of the 3 sections | Incomplete report | `reports/Module<X>_Parity.md` has all sections; it is shown at the final gate |
| ❌ | Pausing to present a module report or asking "proceed?" | Not a designed gate | Write the report and continue; the only stops are gate 1, gate 2 and the baseline exception |
| ❌ | "Test passed" without field-level evidence | No proof of correctness | Show actual captured values: "Contract ID='2394', Status='Active', Rows=8" |
| ❌ | Generic NOT TESTED reasons: "couldn't test" | Not actionable | Specific: "Requires Terminated contract status — no test data available" |
| ❌ | Summary shows PASS but Step Report has empty cells | Inconsistent report | Every step must have Status + Description filled |

### Self-Check Before the Final Report

Before the Phase 1 content goes into the final report, verify:

1. ✅ Step Report table has a row for EVERY step in the module
2. ✅ Every step has Status (✅ PASS / ❌ FAIL / ⚠️ NOT TESTED)
3. ✅ Every PASS step has a plain-English description with actual data values
4. ✅ HTML Diff section exists (even if "0 uncovered diffs" for all steps)
5. ✅ Summary section has assertion counts, step counts, and key baseline values
6. ✅ NOT TESTED items each have a unique, specific justification
7. ✅ No raw selector names or attribute paths in descriptions

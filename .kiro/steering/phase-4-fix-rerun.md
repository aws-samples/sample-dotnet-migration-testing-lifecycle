---
inclusion: manual
---

# Phase 4: Fix & Rerun (auto-fix loop)

Reference: `#phase-4-fix-rerun.md`
Used by: the `target-fixer` agent (workflow `auto-fix` loop) and the `comparison-reporter`
agent on a "fix X" reply at the final gate.

---

## Purpose

Fix functional findings and actionable CSS differences on the **Target** side, then rerun to
verify. **No human gates.** One round covers **all** fixable modules. At most 2 rounds; the
loop stops as soon as `status.json.fixLoop.clean == true`. Findings still open after 2 rounds
are carried into the final report as **UNRESOLVED** with evidence; the loop never pauses on
its budget.

Each round starts with
`.\.kiro\test-project\lifecycle.ps1 -Action progress -Page <PageName> -Phase 3 -Message 'Fix step started'`
so the Fix phase is timed (budget 180 s).

---

## Step 4.1: Start the round

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action fix-round -Page <PageName>
```

Called once per round, before any edit. It prints `round=<n>`, or `round=<n> budget-exhausted`
after 2 rounds (then stop editing; remaining items go to the final report). A "fix X" at the
final gate uses `-Source final-gate`, which is never budget-limited.

Skip the round entirely when `fixLoop.clean == true`, or when the only unclean modules have
a `harnessError` and no open items.

---

## Step 4.2: Classify, then edit (all fixable modules in this round)

Read `runs/Module<X>/summary.json` for every fixable module. Every finding already has a
`kind` from the generator. Follow the investigation order of `#reporting-standards.md`
(A selector → B data → C app):

| Kind | What may be edited |
|------|--------------------|
| `test-selector` | Only selectors, waits and helper calls in the Target test `.kiro/test-project/Modules/{PageName}.Mod{X}/{PageName}Module{X}_v2.cs`. Never expected constants; never remove or loosen assertions. |
| `test-expectation` | Set the Target EQUALS/CONTAINS constant to the Reference constant with the same `(step, field, type)`, copied verbatim. The only allowed constant change. |
| `test-coverage`, and `missingAssertions` / `missingCaptures` of any finding | Add the missing step, assertion (at the Reference position) or `runner.Capture(state, field, <Target value>)` (same step, same position as in the Reference test body, including NAV/PRE). Added in the same round as an `app` / `test-selector` fix on the same key. |
| `app`, actionable CSS diffs | The Target app only: files under the Target source (status.json `paths.target`, appsettings `TargetSourcePath`). |

Never edit Reference tests or the Reference source (status.json `paths.reference`, appsettings `ReferenceSourcePath`). Never weaken an assertion. Rebuild
after any Target test edit:
`.\.kiro\test-project\lifecycle.ps1 -Action build-module -Page <PageName> -Module <X>`.

---

## Step 4.3: Record the fix

Append to `.kiro/app-testing/<PageName>/fixes.json` (JSON array):
`{ round, source, module, findingIds, kind, file, change, before, after, at }` with the exact
before/after snippets. `before` is what an exit-3 revert restores.

---

## Step 4.4: Rerun the Target (all affected modules at once)

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action rerun-target -Page <PageName> -Modules <X,Y>
```

The runner restarts the Target app only when needed (down, or its sources are newer than the
running process), rebuilds it, saves the previous result as `results.prev.json`, reruns the
Target tests with the same parallel schedule as `run-page`, regenerates the reports and
writes before/after counts into `fixLoop.history`
(`[fix-round] round r Module X: findings a -> b, CSS c -> d`).

| Exit | Action |
|------|--------|
| 0 | Continue |
| 1 + `error CS` in `logs/target-build.log` | Fix the app compile error (counts toward the 3 attempts this round), re-run |
| 1 + `error CS` in `logs/build-<X>.log` | Fix the Target test compile error, `build-module -Module <X>`, re-run |
| 1, other | Retry once (failed modules only), then `progress -Message 'HARNESS-ERROR X: <reason>'` and continue |
| 2 | Edit nothing; `progress -Message 'HARNESS-ERROR X: reference baseline unavailable'` per module named; continue |
| 3 | If `logs/target.err.log` shows this round's edit broke the Target start, revert it (from fixes.json) and re-run once; else retry once; then HARNESS-ERROR and continue |

Then `.\.kiro\test-project\lifecycle.ps1 -Action render -Page <PageName> -View fix-round`
and relay it (CHAT-RENDER): one row per fix with finding, module, kind, file, change,
before -> after and the rerun result.

---

## Step 4.5: Re-validate (runner-verified)

- Previously failing assertions now PASS (`results.json` vs `results.prev.json`)
- CSS diff shows match (`actionableCssDiffs`)
- No NEW failures introduced (`fixLoop.history[].modules.X.*Before / *After`)

---

## Step 4.6: Bug Fix Report (part of the final report)

There is no per-round gate. The generated final report's "Bug Fixes" section lists every fix
from `fixes.json` and `fixLoop.history`:

```markdown
## Bug #1: [Title]
**Found in:** Module {X}, Step {N}   **Kind:** app
**What went wrong:** [plain English]
**Root cause:** [technical]
**Fix applied:** [what changed, which file] (before -> after)
**Verified by:** [rerun result, counts before -> after]
```

---

## Exit criteria

The loop ends when `fixLoop.clean == true` (0 open findings, 0 actionable CSS diffs, every
planned module run, no harness errors) or after 2 rounds. Either way the workflow continues
to the final report; open items are listed as UNRESOLVED with evidence.

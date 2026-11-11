---
inclusion: manual
---

# Phase 5 & 6: Module Completion & Final Documentation

Reference: `#phase-5-6-completion.md`
Used by: the `comparison-reporter` agent (workflow step `final-report`, gate 2).

---

## Phase 5: Module Complete

### Step 5.1: Recorded by the runner

`lifecycle.ps1` records each module's result in `status.json` (`modules.X.reference`,
`target`, `openFindings`, `actionableCssDiffs`, `complete`) and in the `PROGRESS.md` module
table. Nothing to update by hand.

### Step 5.2: Artifacts in place

```
runs/Module{X}/{Reference,Target}/results.json   — step data, assertions, captures
runs/Module{X}/summary.json                      — findings with kind, CSS diffs
runs/Module{X}/build.json                        — build-module result (steps, assertion calls)
reports/Module{X}_*.md                           — Parity, CSS_Diff, Performance, ManualTests
chat/NN-module-result-{X}.md                     — module result chat render
timings.json                                     — per-step ms, runs[], lifecycle phases
```

### Step 5.3: All modules in one run

The `run` step runs every planned module in one `run-page -Modules A,B,... -Detach` call:
read-only modules in parallel, mutating modules one at a time. It never stops between
modules.

---

## Phase 6: Final Test Documentation (deterministic)

### Step 6.1: Generate the report

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action progress -Page <PageName> -Phase 4 -Message 'Final report'
.\.kiro\test-project\lifecycle.ps1 -Action report -Page <PageName>
```

`report` regenerates every module's reports and writes `reports/FinalReport.md`
(console: `[report] reports/FinalReport.md written (N modules)`). The report is built by the
Reports tool from `status.json`, `timings.json`, `runs/Module<X>/summary.json`,
`results.json` and `fixes.json`. **No agent writes or edits FinalReport.md.**

### Step 6.2: What FinalReport.md contains

The final-report sections of `#reporting-standards.md`, each module's 5 sections, and the
timing data:

```markdown
### Executive Summary     overall result (FAIL while any finding is open), total lifecycle time, phase table
### Module Results        per module: workflow, data effect, Reference, Target, open findings, CSS, fixes, result
### Functional Gaps       every gap with severity
### CSS Comparison        every CSS diff
### Bug Fixes             before -> after from fixes.json and fixLoop.history
### Manual Tests          NOT_TESTED items with numbered steps
### Limitations           UNRESOLVED findings with evidence, harness errors as NOT_RUN with log paths
Per module: Step Report, Performance Comparison, Functional Gaps, CSS Diff Report, Summary
(Reference baseline values side by side with the Target values)
```

Markdown is the format; no format-preference question. A PDF/Word export is optional and
only on request.

### Step 6.3: Final Sign-Off (GATE 2)

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action gate -Page <PageName> -Gate final_signoff -State pending -ReportPath reports/FinalReport.md
.\.kiro\test-project\lifecycle.ps1 -Action render -Page <PageName> -View final
```

Relay the `final` render (CHAT-RENDER) and ask "Reply approve to sign off, or fix <what>."
Reply handling:
- `approve` (or `confirm` / `yes` / `ok`): write the reply to `gate-response.txt` and run
  `... -Gate final_signoff -State confirmed -ResponseFile .kiro/app-testing/<PageName>/gate-response.txt`.
- `fix X`: `fix-round -Source final-gate`, apply the Phase 4 rules, `rerun-target -Modules X`,
  `report`, `gate ... -State pending`, `render -View final` again.
- Any other feedback: the report is generated, so it is not edited; answer "The report is
  generated; I can fix the Target (fix <what>) or you can approve." and re-present the same
  render.

---

## Runner-Verified Checks

| # | Check | Evidence |
|---|-------|----------|
| 1 | Every planned module run | `modules.X.target.status != "NOT_RUN"` |
| 2 | Every module has its 4 reports | `modules.X.reports.*` |
| 3 | Open items resolved or listed UNRESOLVED | `fixLoop.clean`, FinalReport.md |
| 4 | Final report generated | `reports/FinalReport.md` (`[report] ... written`) |
| 5 | Human final sign-off | `status.json.finalReport.signedOff == true` |

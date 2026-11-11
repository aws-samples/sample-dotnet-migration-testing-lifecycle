---
name: testing-lifecycle
description: "Manual (non-workflow) fallback for the state-machine testing lifecycle in a single chat session: plan, author tests per module (build-module), run all modules through lifecycle.ps1 run-page, auto-fix the Target (max 2 rounds), deterministic final report. Same gates and commands as the test-page workflow."
tools: ["read", "write", "shell", "web"]
---

# Testing Lifecycle Agent (manual fallback)

> **Preferred:** the `test-page` workflow (`.kiro/workflows/test-page.workflow.json`), started
> with "Run test-page for page <PageName>". Use this agent only when workflows are not
> available. It follows the same steps, gates and commands, conversationally in one session
> (without the workflow's parallel author slots).

`<page>` is the page name. The page dir is `.kiro/app-testing/<page>/`. All paths are
relative to the repo root, which must be the Kiro workspace.

## Common rules (every testing agent)

- **Shell:** only `.\.kiro\test-project\lifecycle.ps1 -Action ...`. No `dotnet` and no inline
  scripts (no sleep/poll loops, `Test-NetConnection`, `Get-Content` or `git`).
- **State:** read it with file tools only (status.json, PROGRESS.md, summary.json, logs).
  `lifecycle.ps1` is the only writer of status.json, timings.json, PROGRESS.md and `chat/`.
- **Human text never goes on a command line.** Write it to
  `.kiro/app-testing/<page>/gate-response.txt` with a file tool and pass
  `-ResponseFile .kiro/app-testing/<page>/gate-response.txt`.
- **`-Message` format:** single-quoted, embedded `'` doubled, one line of at most 200 chars,
  no `$` and no backtick.
- **Never edit** `.kiro/test-project/Harness/**`, `.kiro/test-project/Reports/**` or
  the Reference source (status.json `paths.reference`, appsettings `ReferenceSourcePath`).
- **No confirmation questions** outside the gates listed below.
- **Auto-fix** selector, timing and build errors up to 3 times. Then record the evidence and
  continue.
- **Chat renders:** after each step run `-Action render -Page <page> -View <view>`; it prints
  `CHAT-RENDER <abs path>` and a summary. Read that file and paste it into the chat verbatim
  (`#lifecycle-chat-display.md`). No other status messages besides these renders and the
  gate questions.
- **Shell timeouts:** at least 600000 ms for `run-page` (without `-Detach`), `rerun-target`
  and `report`; at least 150000 ms for `wait`.

## Human gates (ONLY stop for these)

1. **Plan sign-off** (after step 1).
2. **Final report sign-off** (after step 5).
3. **Exception:** Reference baseline failed (`run-page` exit 2 that selector/wait fixes
   cannot clear). Comparing against a broken baseline is meaningless.

A second exit 3 (environment) is a failure stop, not a gate: report the doctor `[FAIL]` line
and its fix, then wait for "retry" or "stop". Everything else is autonomous.

## Steps

Read `status.json` first; `status.json.state` and `PROGRESS.md` tell you where a page is.
`.\.kiro\test-project\lifecycle.ps1 -Action status -Page <page>` prints the same summary.

| # | Step | Same as workflow agent | Key commands | Render |
|---|------|------------------------|--------------|--------|
| 1 | Plan (Phase 0) + gate 1 | `test-planner` | `-Action progress -Page <page> -Phase 0 -Message '...'`, `-Action gate -Page <page> -Gate plan_signoff -State pending`, then `... -State confirmed -ResponseFile ...` | `-View plan` |
| 2 | Author tests, one module at a time | `test-author` | `-Action build-module -Page <page> -Module <X>` per module | `-View tests-authored` |
| 3 | Run all modules | `page-runner` | `-Action progress -Page <page> -Phase 2 -Message '...'`, then `-Action run-page -Page <page> -Modules A,B,C,D` (no `-Detach`; it blocks until done and renders each module result itself) | `-View module-result -Module <X>` (written by the runner), `-View run-summary` |
| 4 | Auto-fix, max 2 rounds, stop when `fixLoop.clean == true` | `target-fixer` | `-Action progress -Page <page> -Phase 3 -Message '...'`, `-Action fix-round -Page <page>`, `build-module` after test edits, `-Action rerun-target -Page <page> -Modules <X,Y>` | `-View fix-round` |
| 5 | Final report + gate 2 | `comparison-reporter` | `-Action progress -Page <page> -Phase 4 -Message '...'`, `-Action report -Page <page>` (writes FinalReport.md), `-Action gate -Page <page> -Gate final_signoff -State pending -ReportPath reports/FinalReport.md`, then `... -State confirmed -ResponseFile ...` | `-View final` |

Follow the linked agent file for each step's rules, including the run-step and fix-step
exit-code tables (`.kiro/agents/page-runner.md`, `.kiro/agents/target-fixer.md`). In manual
mode `run-page` runs in the foreground, so its exit code is the run's exit code; the
workflow uses `run-page -Detach` plus `-Action wait` only so it can relay results while the
run is going.

Other runner actions: `-Action doctor` (environment checklist; also runs automatically),
`-Action reset -Page <page>` (archive the run state to `previous-run/<timestamp>/` and start
the page clean; plan files are kept).

## Rules (non-negotiable)

- Reference always runs before (or alongside) Target for the same module; the runner
  enforces the order and the data isolation (mutating modules serialized, service restarted).
- Default status is FAIL; PASS only with the data that proves it.
- Never weaken an assertion; never edit Reference tests in the fix loop; never edit
  the Reference source (status.json `paths.reference`, appsettings `ReferenceSourcePath`).
- CSS comparison is mandatory at every planned state.
- Findings left after 2 fix rounds go to the final report as UNRESOLVED with evidence; do not
  pause on the budget.
- FinalReport.md is generated by `-Action report`; never hand-write it.

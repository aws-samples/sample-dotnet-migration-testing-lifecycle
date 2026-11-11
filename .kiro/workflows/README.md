# Workflows — Deterministic Lifecycle Orchestration

These Kiro workflow recipes move process sequencing and gating out of steering docs and into
the Kiro runtime, which enforces step order deterministically. Steering docs still define
*how* each step does its work; the workflow defines *what runs, in what order, and what gate
must pass before advancing*. For `test-page`, all execution goes through one runner,
`.\.kiro\test-project\lifecycle.ps1`, so agents never improvise shell commands.

User guide: [docs/RUN-TESTING-LIFECYCLE.md](../../docs/RUN-TESTING-LIFECYCLE.md).

## Recipes

| Recipe | Purpose | Input |
|--------|---------|-------|
| `test-page.workflow.json` | Prove parity in under 15 minutes: plan -> author tests in parallel -> run all modules in parallel -> auto-fix (max 2) -> generated final report | `page` |

### `test-page` flow

```
plan (gate 1: plan sign-off)
  |
author  (parallel node, joinPolicy all)
  author-1  author-2  author-3  author-4  author-5  author-6      one module per slot;
  (A)       (B)       (C)       (D)       (E)       (F..N)        unused slots end at once
  |
run      run-page -Detach -> wait loop (relays each module result)
  |      read-only modules in parallel, mutating ones serialized + service restart
  |      [exception gate: Reference baseline failed]
auto-fix (repeat, max 2, stops when status.json fixLoop.clean == true)
  fix    one round over ALL fixable modules -> rerun-target -Modules ...
  |
final-report  lifecycle.ps1 -Action report (no hand-written content)
              (gate 2: final report sign-off)
```

| Step | Agent | Effort | Gate |
|------|-------|--------|------|
| `plan` | `test-planner` | medium | **Gate 1: plan sign-off** (reply `confirm`) |
| `author` (parallel) -> `author-1` .. `author-6` | `test-author` | medium | — |
| `run` | `page-runner` | low | Only the **exception**: Reference baseline failed (exit 2) |
| `auto-fix` (repeat, max 2, `onMaxIterations: continue`) -> `fix` | `target-fixer` | medium | — (stops when `fixLoop.clean == true`) |
| `final-report` | `comparison-reporter` | low | **Gate 2: final report sign-off** (reply `approve` or `fix X`) |

Two designed human gates plus one exception. No other prompts are expected; findings left
after 2 fix rounds go to the final report as UNRESOLVED instead of pausing.

### Phase numbering

Three numberings are in use. The agents use the runner's `-Phase` value; PROGRESS.md and the
chat show "Phase k of 5"; the methodology doc
([testing-lifecycle.md](../steering/testing-lifecycle.md)) uses phases 0-6.

| Displayed ("Phase k of 5") | Runner `-Phase` | Workflow step | Methodology phase (testing-lifecycle.md) |
|---|---|---|---|
| 1 Plan | 0 | `plan` | 0 Analyze |
| 2 Author tests | 1 | `author-1` .. `author-6` | 1 Reference Baseline, 2 Target Capture (writing the tests) |
| 3 Run Reference + Target | 2 | `run` | 1 Reference Baseline, 2 Target Capture, 3 Comparison Reports |
| 4 Fix | 3 | `auto-fix` -> `fix` | 4 Fix & Rerun, 5 Module Complete |
| 5 Report | 4 | `final-report` | 6 Final Documentation |

### 15-minute budget

Phase budgets are the runner's `PhaseBudgetMs` values (shown in PROGRESS.md and in the
`final` render). Phase 3 was measured on PayorList (4 modules: A, B, D read-only, C
mutating); the other phases are agent-driven and not measured yet.

| Phase | Step | Budget | PayorList measured |
|-------|------|--------|--------------------|
| 1 Plan | `plan` | 4:00 | not measured |
| 2 Author tests | `author-1..6` | 4:00 | not measured (`build-module` about 3 s per module) |
| 3 Run Reference + Target | `run` | 4:00 | **2:54** (174 s) in the latest run with the visual checks; 2:37-2:54 before they were added |
| 4 Fix | `auto-fix` | 3:00 per round | estimate 3:15-3:45 per round: runner 1:45-2:15 (from measured parts; `rerun-target -Modules A` 68 s) + fixer agent (not measured) |
| 5 Report | `final-report` | 1:00 | `report` about 3 s |
| **Total (estimate)** | | 15:00 target | clean run about 10:57; 1 fix round about 14:15-14:45; 2 fix rounds about 17:30-18:30 (over target). Plan/Author use budgets; gate time excluded |

Before the visual checks were added, per module in the worst parallel run (Reference / Target wall time): A 71.4 / 66.9 s,
B 51.8 / 49.1 s, D 36.0 / 43.4 s (all six run at once), then C 16.8 / 20.1 s plus two
service restarts (14.9 s and 18.6 s). Startup (doctor, preflight, module builds) is about
20-25 s.

### What the chat shows

Each step renders a view with `lifecycle.ps1 -Action render` and sends a
`CHAT-RENDER <path>` notification. The always-included steering
`.kiro/steering/lifecycle-chat-display.md` makes the main chat publish each file as a markdown
artifact (embedded in the chat) and paste it verbatim below, so you see tables, not one-line
summaries. Each report section is one table. These are the only info messages.

| When | View | You see |
|------|------|---------|
| Gate 1 | `plan` | Modules (user story, steps, assertions, data effect, CSS states), a steps table per module, states, NOT_TESTED |
| Start of `run` | `tests-authored` | Per module: test names, revision, steps, assertion calls, build result |
| Each module finishes | `module-result` | Reference and Target step reports with actual values, performance per step, functional gaps, CSS diffs |
| End of `run` | `run-summary` | Per module pass counts, findings (H/M/L), CSS diffs, ms; Phase 3 wall time |
| Each fix round | `fix-round` | Each fix: finding, kind, file, change, before -> after, rerun result |
| Gate 2 | `final` | Executive summary with phase timings and total time, module results, open items |

### `run-page -Detach` and `wait` (workflow) vs manual `run-page`

A shell call from an agent must return within its timeout, but a full run takes about 3
minutes. So the `run` step starts the run with `run-page -Detach` (returns in about 2 s) and
then repeats `wait -TimeoutSec 90` (agent shell timeout at least 150 s). Each `wait` prints
the new `CHAT-RENDER` lines, which the step relays, and ends with
`[wait] run=running` (call again) or `[wait] run=finished exit=<code>`. Manually you just run
`.\.kiro\test-project\lifecycle.ps1 -Action run-page -Page <Page>` without `-Detach`: it
blocks until done, prints everything to the console and exits with the run's code.

## Before the first run

1. **Open the repo folder itself as the Kiro workspace** (not its parent). The auto-fix
   loop's `fileCheck` path `.kiro/app-testing/{{page}}/status.json` is resolved relative to
   the workspace root, so it only works when the workspace is the repo root.
2. **Install the zero-prompt rules once per clone.** `.kiro/settings/permissions.yaml` is a
   template; Kiro does not read it from the repo. Follow the 4 steps in its header
   (Ctrl+Shift+P → "Kiro: Open Permissions (Workspace)", paste, save, new chat).
3. Install Kiro from https://kiro.dev and sign in. Enable the Kiro user setting
   **"Workflows"** (Kiro Settings → Workflows → enable), then start a new chat session.
   Doctor checks it (`kiroAgent.workflows.enabled`). If the setting is missing, update Kiro
   to the latest version. Workflows docs: https://kiro.dev/docs/workflows/.
4. Optional self-check: `.\.kiro\test-project\lifecycle.ps1 -Action doctor` (also runs
   automatically before every run).

## How to run (Kiro IDE)

1. In a new chat, ask Kiro to run the recipe with the input, e.g.
   *"Run test-page for page PayorList"*.
2. Read the tables in the chat as each phase finishes. For a live view keep
   `.kiro/app-testing/<page>/PROGRESS.md` open: phase line
   (`Phase k of 5 (<name>) - elapsed - estimated remaining`), modules and phase timings.
3. Open **Workflows** above the parent chat to watch steps; the run pauses only at the gates.

**Resume** an interrupted run: start a new chat with
*"Resume test-page for page <PageName>"*. The plan step shows "reply confirm to resume";
modules already run in this session are skipped and the fix budget continues.

**Run history.** Every run has its own id (`run-YYYYMMDD-HHMMSS`) and never reuses an
earlier run's results. Its outputs are kept in `.kiro/app-testing/<page>/history/<runId>/`
with an index in `history/index.json` and `history/README.md`; top-level `reports/` and
`chat/` are the latest run. Prune by deleting old `history/<runId>/` folders and their
`index.json` entries.

**Source paths.** The prompts and agents never name the sample folders. `progress -Phase 0`
writes `appsettings.test.json` `ReferenceSourcePath` / `TargetSourcePath` to `status.json`
`paths.reference` / `paths.target`; the fixer edits only under `paths.target`, and nobody
edits `paths.reference`.

**Reset** a page to a clean state (archives the run state to
`.kiro/app-testing/<PageName>/previous-run/<timestamp>/`, keeps the plan files):
`.\.kiro\test-project\lifecycle.ps1 -Action reset -Page <PageName>`, then run the recipe again.

## Migrating first?

Migrating a page is done in the separate migration-lifecycle repo, in its own Kiro workspace.
See the root README section "Related repo" for the link and the hand-off steps.

## Why these gates are deterministic

- **Ordering** is structural: a step cannot start before its predecessor completes; the
  `author` parallel node joins all six slots (`joinPolicy: all`) before `run` starts. The
  agent cannot jump ahead.
- **Human gates** pause the step with `send_message` severity `warning`; the step stays
  paused until the human replies and `lifecycle.ps1 -Action gate ... -State confirmed`
  exits 0 (no step uses the `need_input` completion signal).
- **Quality/coverage gates** use `fileCheck` against `status.json` (see
  `.kiro/test-project/Specs/status.README.md`): the auto-fix loop stops when
  `fixLoop.clean == true`. For `test-page` the runner owns gate state: only
  `lifecycle.ps1` writes status.json, so the agent cannot fake advancement.
- **Reports are generated**, not written by an agent: `FinalReport.md` and every chat view
  come from the run data.

## Step agents

Defined in `.kiro/agents/`: `test-planner`, `test-author`, `page-runner`, `target-fixer`,
`comparison-reporter`. `testing-lifecycle` is the manual
(non-workflow) fallback. Each loads the relevant steering doc for its phase. No agent or
workflow step pins a `modelId`.

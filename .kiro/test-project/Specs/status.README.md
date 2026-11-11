# `status.json` - Deterministic Lifecycle State (schema v2)

`status.json` is the machine-readable state of one page's testing lifecycle. The workflow's
`fileCheck` stop condition and the step agents read it; humans read the companion
`PROGRESS.md`.

- **One file per page:** `.kiro/app-testing/{Page}/status.json`
- **Single writer:** only `.kiro/test-project/lifecycle.ps1` writes `status.json`,
  `timings.json` and `PROGRESS.md`, atomically (temp file + replace). Agents never edit these
  files; they influence state only through `lifecycle.ps1 -Action gate | progress | fix-round |
  reset` (and the run actions). Agents read the files with file tools.
- **Example:** `status.example.json` (a complete, valid v2 document).
- A v1 file (no `schemaVersion`) is migrated in memory by `run-page`/`reset` etc.: the old
  object is kept verbatim under `previousRun.data` with `previousRun.kind = "v1-migration"`.

## Field contract

| Field | Type | Meaning |
|-------|------|---------|
| `schemaVersion` | `2` | Schema version. |
| `page` | string | Page under test, e.g. `PayorList`. |
| `lifecycle` | `testing` | Lifecycle tracked by this file. |
| `state` | string | `planning`, `plan_pending`, `authoring`, `running`, `fixing`, `reporting`, `report_pending`, `complete`, `failed`, `baseline_failed`. |
| `updatedAt` | ISO-8601 | Last write. |
| `session` | object \| null | `{id, startedAt, planRevision, lastExitCode}`. `id` is the run id `run-YYYYMMDD-HHMMSS` (also the `history/<id>/` folder). `null` after `reset`. See the session rule below. |
| `paths` | object \| null | `{reference, target}`: the source folders of the Reference and Target apps, repo-root relative with forward slashes (`null` when unset), copied from `appsettings.test.json` `ReferenceSourcePath` / `TargetSourcePath` by `progress -Phase 0`. Agents never edit `paths.reference`; Target fixes go only under `paths.target`. |
| `plan` | object | `{signedOff, signedOffAt, signedOffRevision, signedOffFingerprint, fingerprint, fingerprintRevision, response, planFile, modules[]}`. Plan sign-off record (gate 1). `modules` is the planned module order. `fingerprint` is the runner's SHA256 over each module's id, plannedSteps, test names, cssStates and step ids, recorded at every `plan_signoff` gate call; `fingerprintRevision` is the plan revision it belongs to (always equal to plan.json `revision` after the gate). When the fingerprint changed but plan.json kept its revision, the gate sets plan.json `revision` to the recorded revision + 1 itself. `signedOffFingerprint` is the fingerprint that was signed off. |
| `gate` | object | `{pending, name, since, question, reportPath, lastResponse}`. `pending=true` only while a human gate (`plan_signoff`, `final_signoff`, `baseline_failed`) waits. |
| `currentRun` | object \| null | `{runId, action, module, system, step, stepIndex, stepTotal, stepTotalEstimated, startedAt, jobs[]}` for the latest `run-page` / `rerun-target` / `report`. `jobs` = `[{module, system, step, stepIndex, stepTotal}]`, one entry per test job running right now (several while modules run in parallel; empty when the run is done); the single `module`/`system`/`step` fields mirror the first running job. Extra fields: `testStartedAt`, and after the run `finishedAt`, `exitCode`. |
| `apps` | array | `[{name, pid, startedByRunner, url, healthAccept}]` - the app processes found/started by the runner (pid = owner of the listening port). |
| `modules.{X}.name` / `plannedSteps` / `cssStates` / `tests.reference` / `tests.target` | | From `state-map/plan.json` (fallback test names `Reference{Page}_Mod{X}` / `{Page}_Mod{X}`). |
| `modules.{X}.dataEffect` | `read-only` \| `mutating` | From `plan.json modules[].dataEffect` (missing = `mutating`). Drives the schedule: read-only modules run in one parallel batch (at most `-MaxParallel`, default 4); each mutating module runs alone and the data service is restarted after its Reference and after its Target run. |
| `modules.{X}.reference` / `.target` | object | `{status: NOT_RUN\|PASS\|FAIL, stepsPass, stepsFail, stepsNotTested, assertionsTotal, assertionsPassed, assertionsFailed, executedAt, durationMs, abortReason, results}`. The verdict comes ONLY from `runs/Module{X}/{System}/results.json` (the `dotnet test` exit code is ignored). PASS needs 0 failed steps, 0 failed assertions and at least one passed step; a missing/aborted results file or a timeout is FAIL with `abortReason` (`no results.json`, the abort message, or `timeout`). |
| `modules.{X}.openFindings` / `actionableCssDiffs` / `actionableCssRows` / `cssDiffsTotal` / `notTested` | int | Merged from `runs/Module{X}/summary.json` (report generator). `actionableCssDiffs` = actionable page-level CSS groups OWNED by the module (`runs/css_groups.json`; owner = first module in plan order the group was seen in; includes the `layout.*` geometry findings). `actionableCssRows` = raw actionable CSS rows of the module (informational). Ownership can move when another module reports, so lifecycle.ps1 re-reads both counts of every module after each report (`Sync-CssGroupCounts`). |
| `modules.{X}.referenceDefects` | int | `summary.json referenceDefectCount`: baseline issues found by the content checks on the Reference (leaked template syntax, console/page errors). Listed in the reports; never part of `openFindings` or the Target verdict. |
| `modules.{X}.harnessError` | string \| null | Set by `-Action progress -Message 'HARNESS-ERROR <X>: <reason>'`; cleared by the next successful run of that module. |
| `modules.{X}.reports` | object | `{parity, cssDiff, performance, manualTests}` paths under the page folder. |
| `modules.{X}.complete` | bool | Reference PASS **and** Target ran **and** `openFindings == 0` **and** `actionableCssDiffs == 0` **and** `actionableCssRows == 0`. |
| `totals` | object | `{openFindings, actionableCssDiffs}` summed over the planned modules (`actionableCssDiffs` = actionable CSS groups of the page). |
| `fixLoop` | object | `{round, maxRounds: 2, clean, history[{round, source, at, modules{X:{openFindingsBefore, actionableCssBefore, openFindingsAfter, actionableCssAfter}}}]}`. `fix-round` increments `round` and pushes the entry; `rerun-target` writes the `*After` counts and never increments `round`. |
| `finalReport` | object | `{path, signedOff, signedOffAt, response}` (gate 2). |
| `allModulesComplete` | bool | Every planned module is `complete`. |
| `previousRun` | object \| null | `{kind: v1-migration\|reset, archivedAt, path, data}`. |
| `events` | array | Last 50 runner events of type `render-failed`: `{type, view, module, message, runId, at}` (a chat render that failed during a run; the run continues). |
| `progress` | object | **Extra (runner-owned):** `{status, now, next, eta, recentEvents[]}` - the persisted `Status:` / `Now:` / `Next:` / ETA and the last 50 events that `PROGRESS.md` is rendered from. |

## `fixLoop.clean` (the auto-fix stop condition)

Recomputed on every write:

```
clean = totals.openFindings == 0
     && totals.actionableCssDiffs == 0      (actionable CSS groups)
     && every planned module has actionableCssRows == 0
     && every planned module has target.status != NOT_RUN
     && every planned module has harnessError == null
```

The workflow's repeat step stops on `fileCheck: status.json  .fixLoop.clean == true`
(path relative to the repo root, so the repo folder itself must be the Kiro workspace).

## Session rule and resume

- `reset` sets `session = null`.
- `run-page` starts a **new session** when `session == null` or
  `plan.signedOffRevision != session.planRevision`; a new session gets a new
  `id`/`startedAt`, `planRevision = plan.signedOffRevision` (null for a manual run without
  sign-off) and resets `fixLoop` to round 0 with empty history. Otherwise the session
  continues (that is what "resume" means).
- Re-confirming the same plan revision (`gate plan_signoff confirmed` when already signed off
  at that revision) is a no-op apart from an event, so resuming never resets the fix budget.
- **Module skip condition (read by the run step, not enforced by the runner):** module X is
  done for this session when `session != null`, `session.planRevision ==
  plan.signedOffRevision`, `modules.X.reference.status == "PASS"`, `modules.X.target.status !=
  "NOT_RUN"`, `modules.X.harnessError == null` and `modules.X.reference.executedAt >=
  session.startedAt`.

## Gates

| Gate | `pending` | `confirmed` |
|---|---|---|
| `plan_signoff` | Re-imports `state-map/plan.json` into `modules` (unchanged modules - same `id`, `plannedSteps`, test names, `cssStates` - keep their run data; changed ones reset to NOT_RUN; dropped ones are removed). State `plan_pending`. | No-op (event only) if already signed off at the same revision; else `plan.signedOff/At/Revision/response`. State `authoring`. |
| `final_signoff` | Needs `-ReportPath`. State `report_pending`. | `finalReport.signedOff/At/response`. State `complete`. |
| `baseline_failed` | Needs `-ReportPath`. State `baseline_failed`. | Clears the gate, stores the reply in `gate.lastResponse`, state `running`. |

`-ResponseFile` (UTF-8, truncated to 2000 chars) is stored and then deleted. There is no
`-Response` parameter: human text never goes on a command line.

## Reset

`lifecycle.ps1 -Action reset -Page <Page>` moves `status.json`, `fixes.json`, `PROGRESS.md`,
`runs/`, `reports/` and `logs/` into `previous-run/<yyyyMMdd-HHmmss>/`, **copies**
`timings.json` there and keeps it in place (ETA history), keeps `state-map/`, and writes a fresh
v2 `status.json`: modules from `plan.json` (else the old module list) all NOT_RUN,
`plan.signedOff = false`, `session = null`, `previousRun = {kind: "reset", archivedAt, path}`.

## Related files (same folder)

- `timings.json` - `{schemaVersion: 2, page, latest{X{Reference|Target{testOverheadMs, wallMs, steps[{step, description, actionMs, stepMs, status}]}}}, runs[last 10]{runId, sessionId, action, modules, startedAt, finishedAt, exitCode, maxParallel, preflightMs, buildMs, reportMs, restarts[{app, reason, ms, at}], mutating[], tests[{..., startedAt, finishedAt, batch}]}, lifecycles[last 10]{id, page, startedAt, finishedAt, totalBudgetMs, phases[], gates[], postGate}}`. `testOverheadMs = wallMs - sum(stepMs)`. `lifecycles` holds the 5 timed phases (Plan, Author tests, Run Reference + Target, Fix, Report) opened by `-Action progress -Phase 0..4` and by the run actions; gate wait time is excluded from the total.
- `PROGRESS.md` - human view, rewritten on every step event and every gate/progress/fix-round/reset.
- `runs/Module{X}/{System}/results.json`, `runs/Module{X}/summary.json`, `runs/Module{X}/build.json` (build-module result), `runs/Module{X}/{System}/events-<runId>.jsonl`, `runs/last-run.json` `{runId, action, pid, exitCode, message, finishedAt}` - per-run data.
- `chat/NN-<view>[-X].md`, `chat/index.json` `{schemaVersion, page, entries[{seq, view, module, path, at, summary, source, runId}]}` and `chat/relay.json` `{lastSeq, runId, at}` - chat renders (`-Action render`, and the runner after each module and at the end of a run); `-Action wait` prints the ones not yet relayed.
- `logs/` - `build-<X>.log`, `build-harness.log`, `build-reports.log`, `render-NN.log`, `run-page-<ts>.console.log` (detached runs), `target-build.log`, `<app>.log` / `<app>.err.log`, `test-<X>-<System>.log`, `report-<X>.log`, `list-tests.log`, `run-<runId>.log`.
- `history/<runId>/` - a copy of one run's outputs (`reports/`, `chat/`, `runs/`, `timings.json`, `fixes.json`, a `status.json` snapshot), refreshed after every run action, gate and render of that run. `history/index.json` `{page, runs[{runId, startedAt, finishedAt, planRevision, modules{X{reference, target}}, findings, fixRounds, finalSignOff, folder}]}` and `history/README.md` list every run. Top-level `reports/` and `chat/` are the latest run. Prune by deleting old `history/<runId>/` folders and their `index.json` entries.
- `.lock` - `{pid, action, startedAt, runId}` while a run action (`run-page`, `rerun-target`, `report`) runs. `build-module`, `render` and `wait` take no page lock.

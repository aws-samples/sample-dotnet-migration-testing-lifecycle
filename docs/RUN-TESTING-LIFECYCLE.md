# Run the Testing Lifecycle

This guide gets you from a fresh clone to a signed-off parity report for one page: the
migrated page (Target) compared step by step with the legacy page (Reference). You paste one
prompt, answer two questions, and everything else runs on its own.

---

## 1. Prerequisites

**Setup step 1: check the prerequisites.**

- [ ] **Windows 10 or 11** with Windows PowerShell 5.1 (PowerShell 7 should work but is
      untested; the runner is Windows-only).
- [ ] **.NET 10 SDK.** Every project (the test project and the sample apps) targets
      `net10.0`. Check with `dotnet --list-sdks`.
- [ ] **Git**, to clone the repo.
- [ ] **Kiro.** Install Kiro from https://kiro.dev and sign in.
- [ ] **Kiro user setting "Workflows" enabled.** Kiro Settings → Workflows → enable, then
      start a new chat. Doctor checks it (`kiroAgent.workflows.enabled` in
      `%APPDATA%\Kiro\User\settings.json`). If the setting is missing, update Kiro to the
      latest version. Workflows docs: https://kiro.dev/docs/workflows/.
- [ ] **PowerShell may run local scripts.** Once per user:
      `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` (the Windows client default
      `Restricted`, or `AllSigned`, blocks `lifecycle.ps1`). Doctor reports the policy a new
      shell gets.
- [ ] **Ports 5000, 5100 and 5200 free** for the bundled sample (or used by the sample apps).
- [ ] **Internet access** on the first run (NuGet restore, Playwright Chromium download) and
      on every run (the Reference pages load their scripts from public CDNs).

**Setup step 2: clone and open the repo.**

```powershell
git clone https://github.com/aws-samples/sample-dotnet-migration-testing-lifecycle.git
```

In Kiro, use File → Open Folder and pick the cloned folder itself, not the folder above it.

**Setup step 3: install the zero-prompt rules.** Open `.kiro/settings/permissions.yaml` and
follow the 4 steps in its header: with this repo folder open as the workspace, press
Ctrl+Shift+P → "Kiro: Open Permissions (Workspace)", replace that file's contents with the
template's contents and save, then start a new chat. **Skipping this brings back an approval
prompt on every command.** Do it once per clone.

Kiro stores the installed rules outside the repo, in
`%USERPROFILE%\.kiro\workspace-roots\<hash>\permissions.yaml`; doctor checks that the
template's marker line is there. Doctor can't tell which workspace a file belongs to, so a
second clone can show PASS without its own install; if prompts appear, re-install for this
folder.

> **Security note.** The template allows every shell command, every write inside the
> workspace and every file read on the machine without a prompt. It denies only force-push,
> `git reset --hard`, `git clean`, `--no-verify`, secret files (`.env`, keys, `.ssh`, AWS
> credentials) and writes to the Reference source; `git push` and `git commit` still ask.
> Review it before you install it.

**Setup step 4 (optional): self-check**, from the repo root in a terminal:

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action doctor
```

It prints `[PASS]`, `[FAIL]` or `[WARN]` per item, and every `[FAIL]` line ends with the exact
fix. You don't have to run it yourself: it also runs automatically before every test run.

---

## 2. One-time setup for a new app

**Bundled sample (PayorList): nothing to set up.** Skip to section 3.

For your own app, edit `.kiro/test-project/appsettings.test.json`:

| Setting | Set it to |
|---------|-----------|
| `BaseUrl` | Your Target (migrated) app, e.g. `http://localhost:5200` |
| `ReferenceBaseUrl` | Your Reference (legacy) app, e.g. `http://localhost:5100` |
| `Apps[]` | One entry per app the tests need, in start order |
| `Apps[].project` | Path to the app's `.csproj`, relative to `.kiro/test-project/` |
| `Apps[].healthUrl` | A URL that answers when the app is up |
| `Apps[].healthAccept` | `status200` (default, needs HTTP 200) or `anyResponse` |
| `Apps[].isTarget` | `true` on the Target app (it is rebuilt when its code changes) |
| `Apps[].resetData` | `true` on the app whose restart resets the test data |
| `ReferenceSourcePath` | Source folder of the Reference app, relative to the repo root (never edited) |
| `TargetSourcePath` | Source folder of the Target app, relative to the repo root (the only place fixes go) |
| `Auth`, `AuthStateFilePath` | Login URL and user, if the app needs a sign-in |
| `TestEntityId` | The record your tests open, if any |

SOAP/WCF services (`.svc`) usually reject a plain GET, so give them
`"healthAccept": "anyResponse"`. The runner starts apps with `dotnet run --project`; a
non-.NET app, or one hosted elsewhere, must already be running.

Also change the Reference deny path in `.kiro/settings/permissions.yaml` to your
`ReferenceSourcePath` and re-install it (setup step 3). The full per-app list is in the
README, "Starting a New Project".

### Visual comparison settings

The CSS comparison is built for any web app pair (WebForms, MVC, Razor Pages, Blazor, other); so
far only WebForms → Blazor (the bundled sample) has been run end to end. Besides
the elements the planner maps in `state-map/css-map.json`, the harness inventories every visible
control, heading, label, table cell (first rows), image and message on both sides, pairs them by
role, accessible name, order and position, and reports missing/extra elements, label text, styles,
hover/focus/disabled styles, layout relations (order, alignment, spacing, wrapping, overflow,
overlap) and responsive problems at extra widths. It also flags broken images, failed requests,
empty sections and controls without an accessible name. Tune it in the `VisualChecks` block:

| Setting | Default | Set it to |
|---------|---------|-----------|
| `StackProfile` (top level) | `webforms-jqgrid__blazor-mudblazor` in the sample | The profile of your stack pair, or `""` for `generic.json` only |
| `VisualChecks.Inventory.MaxRowsPerGrid` | 3 | Grid rows compared per table |
| `VisualChecks.InteractiveStates.MaxElements` | 12 | Controls hovered/focused per state (lower it to save time) |
| `VisualChecks.Responsive.Widths` | `[1366, 768]` | Extra viewport widths to check (`Responsive.Enabled: false` to skip) |
| `VisualChecks.IgnoreProperties` | `["cursor"]` | Properties never compared on non-interactive elements |
| `VisualChecks.PageHealth.*` | all `true` | Turn single health checks off |
| `VisualChecks.Severity.*` | 1px thresholds, deltaE 3 / 10, contrast 4.5 | When a style difference is LOW, MEDIUM or HIGH (see "Reading CSS groups" below) |
| `VisualChecks.Groups.ChatTop` | 15 | CSS groups shown in the chat views |

All settings and their meaning: `.kiro/test-project/README.md`, "Visual comparison settings".

### Adding a stack profile

A stack profile stops every page from re-accepting the same framework noise. Create
`.kiro/test-project/profiles/<reference-stack>__<target-stack>.json` (e.g.
`mvc-bootstrap3__blazor-fluentui.json`):

```json
{
  "name": "mvc-bootstrap3__blazor-fluentui",
  "extends": "generic",
  "roleEquivalences": [["gridcell", "cell"]],
  "selectorHints": { "grid": { "reference": [".table"], "target": [".fluent-data-grid"] } },
  "accepted": [
    { "property": "disabled.cursor", "reference": "^default$", "target": "^not-allowed$",
      "reason": "Fluent UI shows not-allowed on disabled controls" }
  ],
  "ignoreProperties": [],
  "ignoreRoles": []
}
```

- `accepted[].property` is exact or ends with `*` (`hover.*`); `role`, `element` (substring of the
  element label), `reference` and `target` (regexes on the values) narrow it. `reason` is required:
  the report shows `accepted by profile <name>: <reason>`.
- Only add differences the team agrees are not visible defects. A page's own `css-map.json`
  `accepted` entries win over the profile.
- Set `"StackProfile": "<name>"` in appsettings and run `lifecycle.ps1 -Action doctor`: the
  `Stack profile` check fails when the file (or its `extends` chain) is missing or does not parse.
- `-Action report -Page <page>` re-applies a changed profile without re-running the tests.

---

## 3. The single prompt

Open a new Kiro chat and paste:

```text
Run test-page for page <PageName>
```

For the bundled sample:

```text
Run test-page for page PayorList
```

You don't need to start any apps. The runner starts them when they are down.

---

## 4. What happens next

```
plan (gate 1) -> author tests (6 parallel slots, one module each) -> run all modules
(read-only in parallel, mutating serialized) -> auto-fix (all modules per round, max 2)
-> generated final report (gate 2)
```

1. **The plan appears in the chat as tables**: the modules (user story, steps, assertions,
   data effect, CSS states), a steps table per module with the fields that change and stay
   the same, the states, and the items that can't be automated, with reasons.
2. **Reply `confirm`** (gate 1). Or describe changes; the plan is revised and shown again.
3. **It runs unattended.** One author agent per module writes and builds that module's tests
   at the same time. Then all modules run: read-only modules side by side, modules that save
   data one at a time on fresh data. As each module finishes, its result table appears in the
   chat. Target issues are auto-fixed for up to 2 rounds, each round covering every module.
   For a live view open `.kiro/app-testing/<PageName>/PROGRESS.md` in an editor tab:
   `Phase k of 5 (<name>) - elapsed mm:ss - estimated remaining mm:ss`, the module table and
   the phase timings.
4. **The final report appears in the chat** (summary, phase timings, module results, open
   items); the full report is `.kiro/app-testing/<PageName>/reports/FinalReport.md`, generated
   from the run data.
5. **Reply `approve`** to sign off, or **`fix X`** to have something fixed and re-checked
   (gate 2).

### What the chat shows per phase

| When | View | You see |
|------|------|---------|
| Gate 1 | `plan` | Modules, steps per module, states, NOT_TESTED |
| Start of the run | `tests-authored` | Per module: test names, revision, steps, assertion calls, build result |
| Each module finishes | `module-result` | Reference and Target step reports with actual values, Reference ms vs Target ms per step, functional gaps, CSS diffs |
| Run finishes | `run-summary` | Per module pass counts, findings (H/M/L), CSS diffs, ms, run wall time |
| Each fix round | `fix-round` | Each fix: finding, kind, file, change, before -> after, rerun result |
| Gate 2 | `final` | Executive summary with phase timings and total time, module results, open items |

The workflow steps produce these with `lifecycle.ps1 -Action render`; the always-included
steering `.kiro/steering/lifecycle-chat-display.md` makes Kiro publish each one as a
markdown artifact (embedded in the chat, full size in the artifact panel) and paste it
verbatim below. Each report section is one table. You can
render any view yourself, e.g.
`.\.kiro\test-project\lifecycle.ps1 -Action render -Page PayorList -View plan` (it prints the
path of the markdown file).

No other prompts are expected. The one designed exception is "Reference baseline failed"
(see section 6). **If any other approval prompt appears**, a permission rule is missing:
run `.\.kiro\test-project\lifecycle.ps1 -Action doctor`, then open a GitHub issue with the
exact command shown in the prompt and the doctor output.

---

## 5. Where the results are

All under `.kiro/app-testing/<PageName>/`:

| File | What it tells you |
|------|-------------------|
| `PROGRESS.md` | Live status: phase line, module table (Reference / Target / Findings / CSS groups / Data effect / Duration), phase timings, recent events |
| `chat/NN-<view>.md` | Every table shown in the chat, numbered in order |
| `reports/FinalReport.md` | The report you sign off (generated): per-module results, Reference values next to Target values, fixes before → after, performance, manual tests, unresolved items |
| `reports/Module<X>_Parity.md`, `_CSS_Diff.md`, `_Performance.md`, `_ManualTests.md` | Per-module detail |
| `reports/CSS_Groups.md` | Every page-level CSS group once (theme groups, individual findings, accepted groups) with its owner module |
| `runs/css_groups.json`, `runs/css_rows_all.json` / `.md` | Groups for the fixer; every raw CSS row with its group id and the rule that set its severity (audit) |
| `status.json` | Machine-readable state (written only by the runner) |

**Run history.** Every run is versioned by its run id (`run-YYYYMMDD-HHMMSS`, when it
started) and never reuses an earlier run's results:

| Path | What it holds |
|------|---------------|
| `history/<runId>/` | That run's `reports/`, `chat/`, `runs/` (results, screenshots), `timings.json`, `fixes.json` and a `status.json` snapshot |
| `history/index.json`, `history/README.md` | One row per run: start, finish, results per module, findings, fix rounds, sign-off |
| `reports/`, `chat/` (top level) | Always the latest run |

To free space, delete old `history/<runId>/` folders and remove their entries from
`history/index.json`; `history/README.md` is rewritten from the index after the next run.
A PayorList run takes about 15 MB.

**Reading PASS/FAIL.** Everything starts as FAIL and becomes PASS only when the captured data
proves it. Each PASS row shows the actual values it is based on. A "finding" is a difference
between Reference and Target; "UNRESOLVED" means it was still open after 2 fix rounds;
"NOT_TESTED" items have manual test steps in `Module<X>_ManualTests.md`.

**Reading CSS groups.** A theme migration changes the same style on many elements, in every
state and every module of the page. The report therefore counts **groups**, not raw rows:

- A **theme group** is every row with the same role class (the role, or its profile
  `roleEquivalences` group), the same property and the same normalized Reference and Target
  values, e.g. `G3 | 46: button 'Save', button 'Add', ... | border-radius | 3px | 4px`. It is one
  finding and one fix: a single CSS rule or theme token in the Target.
- **Individual findings** (missing/extra elements, `layout.*`, `responsive.*`, geometry rows) stay
  per element; only identical repeats across states and modules are merged.
- Groups are computed per **page**. Each group is counted once, in the first module of the plan it
  was seen in (`Seen in` lists every module and state). The other modules' CSS reports say
  `see page-level group G7 (owned by Module A)`. `actionableCssDiffs` in status.json and the chat
  `CSS groups` columns count owned groups; the raw-row count stays informational, and a module with
  raw rows never passes.
- Severity follows `VisualChecks.Severity`: 1px font-size/box/text-metric differences, colors with
  CIEDE2000 deltaE below 3 and font-weight 500 vs 600 are LOW (not actionable); a Target contrast
  below 4.5:1 where the Reference passes is HIGH. The audit file `runs/css_rows_all.md` shows the
  rule behind every row's severity, so nothing is hidden by the grouping.
- The chat shows the top 15 actionable groups (`VisualChecks.Groups.ChatTop`) with a
  `N more in reports/CSS_Groups.md` line; `reports/CSS_Groups.md` and FinalReport.md (and its
  PDF/DOCX) list every group.

**Accepting a group.** Groups are never accepted by the fixer. To accept one:

- for this page only, add the property to the `accepted` map of the element in
  `state-map/css-map.json` (`"accepted": { "font-weight": "<reason>" }`; automatic rows of a css-map
  element use its css-map name as element label);
- for every page of the stack pair, add a profile entry with the group's role and values, e.g.
  `{ "property": "border-radius", "role": "button", "reference": "^3px$", "target": "^4px$", "reason": "MudBlazor 4px radius agreed" }`.

Then run `lifecycle.ps1 -Action report -Page <page>`: accepted rows form their own accepted group
(they never merge with open rows) and drop out of the counts.

---

## 6. Troubleshooting

| Problem | What to do |
|---------|------------|
| An app won't start | Read `.kiro/app-testing/<PageName>/logs/<app>.err.log` and `<app>.log` (`service`, `reference` or `target`). Fix the cause, then reply `retry`. |
| Reference baseline failed (the exception gate) | The legacy page itself didn't produce a valid baseline, so comparing would be meaningless. Read the evidence shown, then reply `retry` (e.g. after fixing test data), a correction such as `correct the expected value to X`, or `stop`. |
| Run interrupted (closed chat, restart) | Start a new chat with `Resume test-page for page <PageName>`, then reply `confirm`. Completed modules are skipped. |
| Stale or confusing state | From the repo root run `.\.kiro\test-project\lifecycle.ps1 -Action reset -Page <PageName>`, then paste the single prompt again. The old state is archived under `previous-run/`. |
| An unexpected approval prompt | A permission rule is missing. Check setup step 3, run `.\.kiro\test-project\lifecycle.ps1 -Action doctor`, and open a GitHub issue with the exact command shown in the prompt and the doctor output. |
| A table shows as raw `\| ... \|` text in the chat | The markdown is valid. The chat inserted a workflow update card ("1 workflow running · 1 update") into the message while it was being written, and the text after the card starts mid-table without its header row. Open the embedded artifact, or the `chat/NN-<view>.md` file named in the message: both show the full table. |
| `lifecycle.ps1 cannot be loaded because running scripts is disabled` | The ExecutionPolicy blocks local scripts. Run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, start a new chat, run doctor. If a Group Policy sets it, ask your admin. |
| Doctor's Ports check fails | Another process holds 5000, 5100 or 5200. Doctor names it and prints a `Stop-Process -Id <pid>` fix; or change the port in `appsettings.test.json` and the app's launch settings. |
| A shell call shows `^C` or exit -1 | The command usually finished anyway (a known shell quirk). The workflow recovers from the runner state; manually, run `-Action status -Page <PageName>` or read the log the command writes. |
| History takes too much disk | Delete old `.kiro/app-testing/<PageName>/history/<runId>/` folders and their entries in `history/index.json` (see section 5). |

---

## 7. Expected duration (bundled sample)

Target: the whole lifecycle in under 15 minutes for a simple-to-medium page, excluding the
time you spend at the two gates.

| Phase | Budget | PayorList measured (4 modules, 35 steps) |
|-------|--------|------------------------------------------|
| 1 Plan | 4:00 | not measured (agent-driven) |
| 2 Author tests (parallel) | 4:00 | not measured (agent-driven); each module build about 3 s |
| 3 Run Reference + Target | 4:00 | **2:54** (174 s) in the latest run with the visual checks; 3:31-3:42 (211-222 s) in earlier builds of those checks; 2:37-2:54 before the visual checks were added |
| 4 Fix (per round) | 3:00 | runner part measured: `rerun-target` of Module A alone 68 s; a full A-D round is estimated at 1:45-2:15 (A, B, D in parallel, then C plus a service restart, preflight and builds), plus fixer agent time (not measured, assumed about 1:30) = **about 3:15-3:45 per round** |
| 5 Report | 1:00 | **about 3 s** measured (`-Action report`) |

Estimated totals, excluding the time you spend at the two gates (Plan and Author tests use
their 4:00 budgets because they were not measured; Run uses the slowest measured 2:54):

| Case | Estimate | Within 15:00? |
|------|----------|---------------|
| Clean run (no fix round) | about 10:57 | yes |
| 1 fix round | about 14:15-14:45 | yes, narrowly |
| 2 fix rounds (the maximum) | about 17:30-18:30 | no |

The 15-minute target covers a clean run or one fix round. A page with many findings will
likely use both rounds and take longer. Before the visual checks were added, a PayorList run
(`run-20261028-210238`) passed all 4 modules on both systems with 0 findings and needed no
fix round. With the visual checks, the latest sample run (`run-20261030-112323`) still has 27
actionable CSS groups open (see Known Limitations in the README).

Phase 3 in the slowest parallel run before the visual checks: startup (doctor, preflight, module builds) about
20-25 s; Modules A, B and D on both systems at the same time, 72 s (set by Module A, 13
steps); then Module C, which saves data, alone: 71 s including two service restarts that
give each system fresh data (15 s and 19 s); reports 3-4 s. Pass counts were identical in the
parallel and one-at-a-time runs.

Measured on the dev box that also hosts the three sample apps: before the visual checks,
`run-page` for A-D took 157.4 / 165.1 / 173.6 s wall in three parallel runs and 222.3 s with
`-MaxParallel 1`; with the visual checks it took 211.3-222.5 s in three earlier builds and
174.4 s in the latest one. `rerun-target -Modules A` took 68 s. Phases 1 and 2 and the
fixer agent part of Phase 4 are agent-driven and were not measured; the totals are estimates. A first run on a new
machine is slower because the apps and the builds start cold.

---

## 8. Running it without the workflow

Every phase is a runner action, from the repo root:

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action build-module -Page PayorList -Module A       # build one module's tests
.\.kiro\test-project\lifecycle.ps1 -Action run-page -Page PayorList -Modules A,B,C,D    # run (foreground)
.\.kiro\test-project\lifecycle.ps1 -Action render -Page PayorList -View run-summary     # any chat view
.\.kiro\test-project\lifecycle.ps1 -Action report -Page PayorList                       # FinalReport.md
```

Without `-Detach`, `run-page` blocks until the run is done and exits with its code. The
workflow adds `-Detach` and polls with `-Action wait -Page <PageName> -TimeoutSec 90` only so
it can show each module result in the chat while the run continues. See
`.kiro/test-project/README.md` for every action, parameter and exit code.

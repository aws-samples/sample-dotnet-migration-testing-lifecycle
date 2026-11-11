# Testing Lifecycle for Kiro (.NET web app parity testing)

> **First time running the parity tests?** Follow the one-page guide:
> [docs/RUN-TESTING-LIFECYCLE.md](docs/RUN-TESTING-LIFECYCLE.md).

A clone-and-run Kiro project for **proving functional + visual parity** between a page of an
old .NET web app and the same page rebuilt on a new UI stack (for example WebForms, MVC or
Razor Pages to Blazor or another stack), through automated, gated comparison testing.

The bundled sample and the tested path is **ASP.NET WebForms (jqGrid) → Blazor (MudBlazor)**.
That is the only pair that has been run end to end. Other pairs are supported by design: the
harness compares two running web apps by URL, and stack-specific noise lives in settings and
stack profiles (`.kiro/test-project/profiles/`), not in the engine.

It ships with everything a developer needs to experiment on their own machine: the Kiro
steering docs, workflow recipes, step agents, hooks, the Playwright/.NET test harness, and a
**bundled sample project** (`/sample`) with a working WebForms-style app and a Blazor version
of it.

## Contents

- [Overview](#overview)
- [Scope](#scope)
- [What gets checked](#what-gets-checked)
- [Repository Layout](#repository-layout)
- [Prerequisites](#prerequisites)
- [Quick Start (how to run)](#quick-start-how-to-run)
- [How the Testing Lifecycle Works](#how-the-testing-lifecycle-works)
- [Starting a New Project (different app)](#starting-a-new-project-different-app)
- [Known Limitations](#known-limitations)
- [Related repo](#related-repo)
- [Bundled sample](#bundled-sample)
- [Cleanup](#cleanup)
- [Security](#security)
- [License](#license)

---

## Overview

You are moving a web app from an **old system (Reference)** to a **new one (Target)**, for
example **ASP.NET WebForms → Blazor**, and you need proof the new pages behave and look like
the old ones. This project **proves it's correct**: a Playwright/.NET harness drives both the
old and new page through the same human-like workflows, captures exact field values and CSS,
and reports every difference as PASS/FAIL **with the data points behind each verdict**.

Migrating the page first is done in the migration-lifecycle repo (see [Related repo](#related-repo)).

What makes it reliable: the process is driven by **Kiro workflows**, so the Kiro runtime
enforces step order and quality gates. For `test-page` the agent can't skip ahead or mark
something done without the evidence in `status.json`, which only the runner writes.

> **Terminology:** *Reference* = existing/legacy app (source of truth). *Target* = new/migrated
> app. *Module* = one complete user workflow tested end to end (e.g. "Create User",
> "Edit & Save", "Grid Sort & Pagination").

---

## Scope

**In scope**

- Page-by-page parity of two running web apps reachable by URL: the old page (Reference) and
  the new page (Target).
- Functional checks through human-like modules (sequences of steps), with actual values on
  both sides and Reference ms vs Target ms per step.
- Visual/CSS checks: automatic element matching by role, accessible name, order and position
  (plus `css-map.json` overrides); normalized computed styles, including hover, focus and
  disabled styles; page-level theme groups with a plain-English Issue sentence; severity from
  CIEDE2000 color difference and WCAG contrast; geometry/layout checks (gaps, row heights,
  order, alignment, wrapping, overflow, overlap) and extra viewport widths (default 1366 and
  768 px).
- Page health on both sides: leaked server/template syntax (`<%`, `@code`, `{{`, `undefined`,
  ...), console and page errors, broken images, failed requests, empty sections and
  accessibility gaps. Old-page (Reference) defects are reported separately and never fail the
  Target.
- Auto-fix of the Target, at most 2 rounds. The Reference source is never edited.
- Versioned runs (`run-YYYYMMDD-HHMMSS`) with markdown, PDF and DOCX reports and per-step
  screenshots.

**Out of scope**

- Migrating pages (done in the migration-lifecycle repo, see [Related repo](#related-repo)).
- Backend, API or service parity (only what the page shows is compared).
- Data migration and database comparison.
- Performance or load testing beyond per-step timing.
- Security testing.
- Cross-browser testing: the harness runs Chromium only.
- Mobile or native apps.
- Non-Windows runners (`lifecycle.ps1` is Windows-only).
- Apps that cannot be started with `dotnet run`, unless they are already running when the
  workflow starts.
- Apps that need a login, unless you configure `Auth` (the login path is untested).
- Pixel-level screenshot diffs and vision-model review (not built; screenshots are evidence only).
- Production use without reviewing the zero-prompt permissions template (see Quick Start step 2).

---

## What gets checked

Each check below runs on both pages at every planned state. LOW rows are listed but not
actionable; MEDIUM and HIGH rows are actionable and keep the module from passing. Method:
[css-comparison-methodology.md](.kiro/steering/css-comparison-methodology.md); report formats:
[reporting-standards.md](.kiro/steering/reporting-standards.md).

| Check | What it catches | Where it shows up |
|-------|-----------------|-------------------|
| Functional steps | Wrong values after an action (sort, filter, paging, save, undo), missing messages, different enabled/disabled state | Module step reports and Functional Gaps (chat `module-result`, `reports/Module<X>_Parity.md`) |
| CSS groups | Missing or extra elements, label text, styles (color, font, border, padding, ...) and hover/focus/disabled styles; the same difference on many elements is one theme group with one fix | `reports/CSS_Groups.md`, `Module<X>_CSS_Diff.md`, top 15 in the chat |
| Layout / geometry | Grid height, row and header height, gap to pager or filter panel, reading order, alignment, wrapping, overflow, overlap | Same CSS tables (`layout.*` rows) |
| Narrow viewport | Elements that overflow, wrap, are clipped or hidden at extra widths (`VisualChecks.Responsive.Widths`) | Same CSS tables (`responsive.*` rows) |
| Page health | Leaked server/template syntax, console and page errors, broken images, failed requests, empty sections, accessibility gaps on the Target | Functional Gaps (kind `app`, field `page.*`) |
| Reference defects | The same health problems on the old page (Reference) | `Reference Issues` in the chat and FinalReport.md; never counted against the Target |
| Severity | HIGH = visually noticeable or different behavior; MEDIUM = size/spacing above the thresholds or different messaging; LOW = no visual impact (1 px, deltaE below 3, font fallbacks) | Severity column; rule per row in `runs/css_rows_all.md` |

Thresholds live in the `VisualChecks` block of `.kiro/test-project/appsettings.test.json`; see
[Visual comparison settings](docs/RUN-TESTING-LIFECYCLE.md#visual-comparison-settings).

---

## Repository Layout

```
.
├── .kiro/
│   ├── workflows/        ← Deterministic orchestration (test-page) + README
│   ├── agents/           ← Workflow step agents (planner, test author, page runner, target fixer, reporter)
│   ├── steering/         ← Methodology + app-specific knowledge (see steering/README.md)
│   ├── hooks/            ← Event-driven hook (report accuracy check)
│   ├── settings/         ← permissions.yaml: zero-prompt rules TEMPLATE (install once, see its header)
│   ├── test-project/     ← lifecycle.ps1 (the runner) + C#/.NET 10 Playwright + NUnit code
│   │   ├── Harness/      ← shared test library (runner engine, CSS, reports, chat renders)
│   │   ├── Harness.Tests/← unit tests (headless; LoginSetupTest is the interactive login helper)
│   │   ├── Reports/      ← report + chat-render console tool
│   │   ├── Modules/      ← one test project per module: {Page}.Mod{X}/
│   │   ├── profiles/     ← stack profiles (generic + webforms-jqgrid__blazor-mudblazor)
│   │   └── Specs/        ← status.json schema/example (the gate's source of truth)
│   └── app-testing/      ← created at run time per page (results, run history); git-ignored
├── sample/               ← Bundled CoreWcf sample: WebForms + Blazor + WCF service (PayorContracts.sln)
│   ├── PayorContracts.Host/   ← CoreWCF service   (http://localhost:5000)
│   ├── PayorContracts.Legacy/ ← WebForms (Reference, http://localhost:5100/Pages/PayorList.aspx)
│   ├── PayorContracts.Web/    ← Blazor  (Target,    http://localhost:5200/Pages/PayorList)
│   ├── README.md         ← What the sample is (all data is fictional)
│   └── RUNNING.md        ← How to run the three apps by hand (the runner starts them for you)
├── docs/
│   ├── RUN-TESTING-LIFECYCLE.md ← User guide: run the parity tests end to end
│   └── SAMPLE-DATA.md    ← The fictional sample data and what the tests rely on
├── LICENSE               ← MIT-0
├── THIRD-PARTY-NOTICES.md← Packages, versions and licenses
├── SECURITY.md           ← Security considerations and vulnerability reporting
├── CONTRIBUTING.md       ← How to contribute
├── CODE_OF_CONDUCT.md    ← Code of conduct
└── README.md             ← This file
```

Methodology vs app-specific steering docs are distinguished by filename; see
`.kiro/steering/README.md`.

---

## Prerequisites

- **Windows 10 or 11** with **Windows PowerShell 5.1** (PowerShell 7 should work but is
  untested). The runner
  (`lifecycle.ps1`) is Windows-only: it uses `Get-NetTCPConnection`, `taskkill` and
  `%APPDATA%\Kiro`.
- **.NET 10 SDK** (every csproj targets `net10.0`: the test projects and every sample app).
  Check with `dotnet --list-sdks`.
- **Git**, to clone the repo.
- **Kiro.** Install Kiro from https://kiro.dev and sign in. Enable the user setting
  **"Workflows"** (Kiro Settings → Workflows → enable), then start a new chat session.
  Workflows docs: https://kiro.dev/docs/workflows/. Doctor checks it
  (`kiroAgent.workflows.enabled` in `%APPDATA%\Kiro\User\settings.json`). If the setting is
  missing, update Kiro to the latest version. (No special IAM/AWS permissions are required to run workflows;
  they run on your normal Kiro sign-in. Workflows use more tokens than a single chat.)
- **PowerShell may run local scripts.** The agents call `.\.kiro\test-project\lifecycle.ps1`
  directly, which fails under the `Restricted` (Windows client default) or `AllSigned`
  policy. Once per user:
  ```powershell
  Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
  ```
  Doctor reports the policy a new shell gets. A Group Policy setting can't be overridden this
  way; ask your admin.
- **Free ports 5000, 5100 and 5200** for the bundled sample (service, Reference, Target).
  Doctor's Ports check names any other process that holds one.
- **Playwright Chromium** is installed automatically by the runner after the first build.
- **Internet access** on the first run (NuGet restore, Playwright Chromium download) and on
  every run (the Reference pages load their scripts from public CDNs).
- Self-check any time: `.\.kiro\test-project\lifecycle.ps1 -Action doctor` (it also runs
  automatically before every test run and prints the exact fix for anything missing).

---

## Quick Start (how to run)

The full walkthrough is in [docs/RUN-TESTING-LIFECYCLE.md](docs/RUN-TESTING-LIFECYCLE.md).

### 1. Open the repo folder itself as the Kiro workspace
```
git clone https://github.com/aws-samples/sample-dotnet-migration-testing-lifecycle.git
```
(see [Related repo](#related-repo) for both repository URLs)

Open this repo folder itself in Kiro (File → Open Folder → the cloned
`sample-dotnet-migration-testing-lifecycle` folder), not a parent folder that holds several repos (for
example next to the migration-lifecycle repo). Then enable Workflows (above).
The workflow's file checks are relative to the repo root, so they only work this way.

### 2. Install the zero-prompt rules once
`.kiro/settings/permissions.yaml` ships the zero-prompt rules as a template. Kiro does not
read it from the repo, so install it once per clone with the 4 steps in its header
(Ctrl+Shift+P → "Kiro: Open Permissions (Workspace)", paste the file's contents, save, start
a new chat). Skipping this brings back an approval prompt on every command.

Kiro stores the installed copy outside the repo, in
`%USERPROFILE%\.kiro\workspace-roots\<hash>\permissions.yaml`. Doctor's "Zero-prompt rules"
check looks for the template's marker line there. Doctor can't tell which workspace a file
belongs to, so a second clone can show PASS without its own install; if prompts appear,
re-install for this folder.

> **Security note.** The template allows every shell command, every write inside the
> workspace and every file read on the machine without a prompt. It denies only force-push, `git reset --hard`, `git clean`,
> `--no-verify`, reads and writes of secret files (`.env`, keys, `.ssh`, AWS credentials) and
> writes to the Reference source; `git push` and `git commit` still ask. Anything else, for
> example `Remove-Item -Recurse` or a download, runs without asking. Review the file before you
> install it, and change the Reference deny path to your app (see Starting a New Project).

### 3. Check your machine (optional)
From the repo root:
```powershell
.\.kiro\test-project\lifecycle.ps1 -Action doctor
```
It prints `[PASS]`, `[FAIL]` or `[WARN]` per item, with the exact fix for every `[FAIL]`. It
also runs automatically before every test run.

### 4. Run a lifecycle via a workflow
In a new Kiro chat, paste:
```text
Run test-page for page PayorList
```

You do not start the sample apps or copy any settings: `appsettings.test.json` is pre-wired
for the sample, and the runner starts the service, Reference and Target apps when they are
down.

### 5. You are asked exactly twice
**The plan** (reply `confirm`) and **the final report** (reply `approve`, or `fix X` to have
something fixed and re-checked). The only other stops are exceptions: the Reference baseline
itself failed, or the environment check failed twice (doctor prints the fix; reply `retry`).

### 6. Read the results
Each phase shows its outcome in the chat as tables: the test plan (modules, steps per
module, states), the tests authored, one result table per module as it finishes, the run
summary, each fix round and the final report. Each view is also published as a markdown
artifact in the chat; if a table ever shows as raw `| ... |` text, open the artifact or the
`chat/*.md` file (see Troubleshooting in the user guide).

The files are under `.kiro/app-testing/<Page>/`:

- `PROGRESS.md`: live view (phase line with elapsed and estimated remaining time, module
  table, phase timings).
- `reports/`: the latest run's reports (`FinalReport.md`, `CSS_Groups.md`, per-module
  `Module<X>_*.md`, PDF/DOCX).
- `chat/`: every table shown in the chat, numbered in order.
- `history/<runId>/`: a full copy of each run (see [Run history](#run-history)).

### 7. (Optional) Run the harness directly
From the repo root, the runner works standalone:
```powershell
.\.kiro\test-project\lifecycle.ps1 -Action doctor
.\.kiro\test-project\lifecycle.ps1 -Action build-module -Page PayorList -Module A
.\.kiro\test-project\lifecycle.ps1 -Action run-page -Page PayorList -Modules A,B,C,D
.\.kiro\test-project\lifecycle.ps1 -Action render -Page PayorList -View run-summary
.\.kiro\test-project\lifecycle.ps1 -Action status -Page PayorList
```
Without `-Detach`, `run-page` runs in the foreground until done (the workflow uses
`-Detach` + `-Action wait` to relay results while it runs). See
`.kiro/test-project/README.md` for every action, parameter and exit code.

---

## How the Testing Lifecycle Works

**Testing Lifecycle** (`test-page` workflow, target under 15 minutes):

```
plan (gate 1) -> author tests (6 parallel slots, one module project each)
  -> run all modules (read-only in parallel, mutating serialized on fresh data)
  -> auto-fix the Target (all modules per round, max 2 rounds)
  -> generated final report (gate 2)
```

The only other stops are exceptions: the Reference baseline itself fails, or the environment
check fails twice (doctor prints the fix; reply `retry`). Test cases
are designed as human-like workflows (modules of sequential steps), and every result is
PASS/FAIL with the exact data points behind the verdict. Each phase is shown in the chat as
tables (`CHAT-RENDER`, see `.kiro/steering/lifecycle-chat-display.md`).

| Phase | Budget | PayorList measured (4 modules) |
|-------|--------|--------------------------------|
| 1 Plan | 4:00 | not measured (estimate = budget) |
| 2 Author tests | 4:00 | not measured (estimate = budget; about 3 s build per module) |
| 3 Run Reference + Target | 4:00 | 2:54 (174 s) in the latest run with the visual checks; earlier builds of those checks 3:31-3:42 (211-222 s) |
| 4 Fix (per round, max 2) | 3:00 | estimate 3:15-3:45 per round (runner 1:45-2:15 from measured parts + fixer agent) |
| 5 Report | 1:00 | about 3 s |
| **Total (estimate)** | 15:00 | clean run about 10:57; 1 fix round about 14:15-14:45; 2 fix rounds about 17:30-18:30 (over target). Human gate time excluded |

Details: [docs/RUN-TESTING-LIFECYCLE.md](docs/RUN-TESTING-LIFECYCLE.md#7-expected-duration-bundled-sample).

Gates: human gates pause the workflow step (`send_message` warning) until the runner records
the reply (`lifecycle.ps1 -Action gate`); the auto-fix loop reads
`.kiro/app-testing/<Page>/status.json` (`fixLoop.clean`) via workflow `fileCheck`. For
`test-page` the runner owns gate state: only `lifecycle.ps1` writes those fields, so the agent
cannot fake advancement. See `.kiro/workflows/README.md` and
`.kiro/test-project/Specs/status.README.md`.

### Run history

Every `test-page` run is versioned by its run id (`run-YYYYMMDD-HHMMSS`, the time it started)
and never reuses an earlier run's results. Under `.kiro/app-testing/<Page>/`:

- `history/<runId>/`: that run's `reports/`, `chat/`, `runs/` (results and screenshots),
  `timings.json`, `fixes.json` and a `status.json` snapshot.
- `history/index.json` and `history/README.md`: one row per run (start/finish, results per
  module, findings, fix rounds, sign-off).
- Top-level `reports/` and `chat/`: always the latest run.

To prune, delete old `history/<runId>/` folders and remove their entries from
`history/index.json` (`README.md` is rewritten from the index after the next run). A
PayorList run adds about 15 MB, mostly screenshots and the TestCaseResults documents.
`.kiro/app-testing/` is git-ignored.

---

## Starting a New Project (different app)

Clone the repo fresh, then make these per-app edits (the reusable methodology stays):

1. `.kiro/test-project/appsettings.test.json` (see section 2 of
   [docs/RUN-TESTING-LIFECYCLE.md](docs/RUN-TESTING-LIFECYCLE.md)):
   - `BaseUrl` (Target) and `ReferenceBaseUrl` (Reference).
   - `Apps[]`: one entry per app in start order (`project`, `healthUrl`, `healthAccept`,
     `isTarget` on the Target app, `resetData` on the app that holds the test data).
   - `ReferenceSourcePath` and `TargetSourcePath`: the two apps' source folders, relative to
     the repo root. The agents never edit the Reference source and fix the Target only under
     `TargetSourcePath` (they read both from `status.json` `paths.*`).
   - `StackProfile`: the profile of your stack pair in `.kiro/test-project/profiles/`, or `""`
     for `generic.json` only. To add one, see
     [Adding a stack profile](docs/RUN-TESTING-LIFECYCLE.md#adding-a-stack-profile).
   - `Auth` (`LoginUrl`, `Username`) and `AuthStateFilePath` if the app needs a login.
   - `TestEntityId` if your tests open one specific record.
2. `.kiro/settings/permissions.yaml`: change the Reference deny path
   (`**/sample/PayorContracts.Legacy/**`) to your Reference source path, then re-install the file in
   Kiro (Quick Start step 2).
3. The app-specific steering registries: `.kiro/steering/selector-registry.md` and
   `css-guide.md` → reset to empty or your app's values.
4. `.kiro/test-project/Harness/Helpers/AppPageHelper.cs` → your app's selectors.
5. Delete the sample module projects `.kiro/test-project/Modules/*` and any
   `.kiro/app-testing/` folder (the workflow regenerates both, including
   `state-map/plan.json` and `css-map.json` from the plan step).
6. `/sample` → replace with your own Reference/Target apps (or delete it).

Keep as-is (reusable core): `.kiro/workflows/`, `.kiro/agents/`, all methodology steering
docs (`testing-lifecycle.md`, phase docs, standards), the test harness engine,
`profiles/generic.json`, and the report-accuracy hook.

Things to know for another app:
- The runner starts apps with `dotnet run --project <csproj>`. A non-.NET app, or one that
  runs elsewhere, must already be running before the workflow starts (an app whose
  `healthUrl` already answers is not started).
- The workflow has 6 parallel author slots. A plan with more than 6 modules still works:
  modules 6 and later are written one after another in slot 6.

See `.kiro/steering/README.md` for the full "where does a new discovery go" routing guide.

---

## Known Limitations

- **Only one stack pair tested.** Only WebForms (jqGrid) → Blazor (MudBlazor), on the bundled
  sample, has been run end to end. Other pairs need their own settings and, usually, a stack
  profile.
- **Workflows are opt-in.** A cloner must enable the Workflows setting in Kiro (see
  Prerequisites).
- **Hooks are agent-mediated.** The `enforce-report-accuracy` hook injects guidance; it is
  not a hard OS-level block. Deterministic sequencing lives in the workflow, not the hook.
- **CSS element matching is heuristic.** Elements are paired automatically by role,
  accessible name, order and position (`css-map.json` entries override). Names are compared
  after lower-casing and trimming `:` / `*`, but a control whose role or name differs a lot
  between the stacks (a native drop-down vs a custom one, a renamed label, a synonym) can show
  up as missing on one side and extra on the other. Header-less layout tables (for example
  WebForms/jqGrid pagers) are excluded from matching by a heuristic; their text is still
  checked. Hover and focus styles are forced one element at a time (Chromium DevTools
  protocol, first `VisualChecks.InteractiveStates.MaxElements` controls per state), so effects
  on other elements are not seen. Treat the CSS diff as a strong signal, not gospel.
- **Known open items on PayorList.** The latest sample run (`run-20261030-112323`) has 27
  actionable CSS groups. A few are likely false positives that planned refinements address:
  the 'Status:' and 'Records per Page' drop-downs reported missing on the Target, a
  self-overlap of the 'Active' checkbox, and the old page's 508.5 px gap above the pager,
  which is labeled an old-page defect but still counted.
- **Gate pause timing not measured.** The author slots wait for plan sign-off through the
  documented `send_message` warning behavior. Full runs have completed (see `history/`), but
  the pause itself was never checked with a timed live test.
- **Untested failure paths.** Abandoned-lock takeover, a render failure, `wait run=crashed`, a
  real dirty-data restart, the 20-minute test kill, dropping a module after a build failure,
  Playwright auto-install, the stale-Target rebuild and PowerShell 7 have not been exercised.
- **15-minute budget only partly measured.** Phase 3 and Report are measured; Plan, Author
  and the fixer agent are estimates. Two fix rounds exceed 15 minutes.
- **Step counts differ by view.** `tests-authored` counts the NAV (page-open) step, other
  views don't (for example 10 vs 9 steps); the view says so in a note.
- **NAV step has no screenshot.** The TestCaseResults documents list it as a step without a
  Reference or Target screenshot.
- **Reference / Target ms** in the chat are sums of step times, not process wall time.
- **Shell `^C` / exit -1.** Agent shell calls can report `^C` or exit -1 although the command
  finished. Recovery relies on the runner state (`-Action wait`, `status.json`).
- **Disk use.** Screenshots add about 5 MB per run, and the TestCaseResults PDF/DOCX are about
  3 MB each; `history/` keeps every run (see Run history for pruning).
- **The sample Reference is a simulation.** `sample/PayorContracts.Legacy` is ASP.NET Core serving the
  `.aspx` pages as static HTML (`Program.cs`), not real WebForms.
- **Windows-only runner, Chromium only.** The runner starts apps with `dotnet run`; other apps
  must already be running. Tests run in Chromium only.
- **Auth path not exercised.** The login flow (`Auth.LoginUrl`, `LoginSetupTest`) is untested:
  the sample has no auth.
- **Sample packages.** The sample packages are pinned to releases with no known advisories
  (`dotnet list sample/PayorContracts.sln package --vulnerable --include-transitive` reports
  none). The sample is not part of the 0-warning gate for the test tooling, and no warning is
  suppressed.

---

## Related repo

Migrating a page (WebForms -> Blazor, `migrate-page` workflow) lives in its own repo, opened
as its own Kiro workspace: **migration-lifecycle**.

Repository URLs:

- Testing lifecycle (this repo): https://github.com/aws-samples/sample-dotnet-migration-testing-lifecycle
- Migration lifecycle: https://github.com/aws-samples/sample-dotnet-migration-lifecycle

Hand-off contract between the two repos (no file is exchanged):

- **Migration output:** the Target source changed in the app folder, plus that repo's
  `.kiro/app-migration/<Page>/status.json` with `validation.gatePassed == true`. This repo does
  not read that file.
- **Testing input:** `.kiro/test-project/appsettings.test.json` here: `ReferenceSourcePath`,
  `TargetSourcePath`, `Apps[]`, `BaseUrl`, `ReferenceBaseUrl`, pointing at the same app.
- **Shared real app:** keep it in one folder next to both repos (for example a sibling
  `MyApp/`) and use repo-relative paths, never absolute ones: `"ReferenceSourcePath":
  "../MyApp/Legacy"`, and `Apps[].project` relative to `.kiro/test-project` (for example
  `../../../MyApp/...`). The runner joins paths with `Join-Path`, which turns an absolute
  second path into an invalid one, so absolute paths break doctor and app startup.
- **Fixing a Target outside this repo (NOT VERIFIED):** the permissions template only allows
  writes under `./**`. Add an `fs_write` allow rule for the Target folder and point the
  Reference deny rule at that app's Reference source before re-installing the template.

Then, in this repo: `Run test-page for page <Page>`.

---

## Bundled sample

`sample/` is a copy of the `sample/` folder of the migration-lifecycle repo (see
[Related repo](#related-repo)). It matches that folder except for the Target PayorList files,
which carry later fixes from test runs in this repo:
`sample/PayorContracts.Web/Components/Pages/PayorList.razor`, `PayorList.razor.cs` and
`wwwroot/app.css`. All
sample data is fictional (see [docs/SAMPLE-DATA.md](docs/SAMPLE-DATA.md)). The Target page
`sample/PayorContracts.Web` (`Components/Pages/PayorList.razor`) is the output of
`Run migrate-page for page PayorList` in that repo, so this repo tests what the migration
produced. The Reference is `sample/PayorContracts.Legacy` and the data service is
`sample/PayorContracts.Host` (solution `sample/PayorContracts.sln`).

To refresh it from a newer migration-lifecycle commit (run in this repo's root, with the
migration-lifecycle clone next to it):

1. Delete `sample/` (including any `bin/` and `obj/` folders), then export the folder from a
   commit, never from that repo's working tree:
   `git -C ../migration-lifecycle archive <commit> sample -o $env:TEMP\sample.tar; tar -xf $env:TEMP\sample.tar -C .`
   (keep the Target PayorList files listed above if the migration copy does not have their fixes)
2. If the project names changed, update `.kiro/test-project/appsettings.test.json`
   (`ReferenceSourcePath`, `TargetSourcePath`, `Apps[].project`). The Harness.Tests
   `WorkflowContractTests.AppSettingsSourcePaths_Exist` test fails while any of these paths
   does not exist.
3. If the Reference folder name changed, update the deny path in
   `.kiro/settings/permissions.yaml` (today `**/sample/PayorContracts.Legacy/**`), then
   re-install the template (Quick Start step 2: "Kiro: Open Permissions (Workspace)", paste,
   save, start a new chat).
4. Run the module tests (`Run test-page for page PayorList`). Reference tests change only when
   the Reference page changed; Target failures are migration gaps to report, not test bugs.

---

## Cleanup

- Stop the sample apps: `Get-Process -Name "PayorContracts*" | Stop-Process`.
- Delete the local run data in `.kiro/app-testing/`.
- Remove the installed permission rules from Kiro.

See [SECURITY.md](SECURITY.md#resource-cleanup) for the full list.

## Security

This is sample code for demonstration and learning. It is not production-ready. See
[SECURITY.md](SECURITY.md) for the known security considerations, hardening recommendations
and how to report a vulnerability.

## License

This project is licensed under the MIT-0 License. See the [LICENSE](LICENSE) file.

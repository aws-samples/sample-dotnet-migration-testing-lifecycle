---
name: target-fixer
description: "Step 'fix' inside the test-page auto-fix loop (max 2 rounds). One round covers ALL fixable modules: classifies each open finding by the kind the report generator assigned, then edits only what that kind allows (Target test selectors/waits, Target EQUALS/CONTAINS constants aligned to Reference, missing mirrored steps/assertions/captures, or the Target app), rebuilds with build-module and re-verifies with lifecycle.ps1 rerun-target -Modules. Never edits Reference tests or the Reference app. Never pauses."
tools: ["read", "write", "shell"]
---

# Target Fixer (workflow step `fix`, Phase 3)

> Phase numbers in this file are the runner's `-Phase` values (Phase 3 shows as "Phase 4 of
> 5"). Mapping: [Phase numbering](../workflows/README.md#phase-numbering).

You close open findings and actionable CSS groups on the **Target** side. One loop iteration
is one round over **all** fixable modules, verified through the runner. The loop stops by
itself when `status.json.fixLoop.clean == true` or after 2 rounds; findings that remain go to
the final report as UNRESOLVED. You never pause.

`<page>` below is the workflow input `page`. The page dir is `.kiro/app-testing/<page>/`.
All paths are relative to the repo root, which is the Kiro workspace.

## Common rules (every testing agent)

- **Shell:** only `.\.kiro\test-project\lifecycle.ps1 -Action ...`. No `dotnet` and no inline
  scripts (no sleep/poll loops, `Test-NetConnection`, `Get-Content` or `git`). Test builds go
  through `-Action build-module`.
- **State:** read it with file tools only (status.json, summary.json, results.json, logs).
  `lifecycle.ps1` is the only writer of status.json, timings.json, PROGRESS.md and `chat/`.
- **Human text never goes on a command line.** Write it to
  `.kiro/app-testing/<page>/gate-response.txt` with a file tool and pass `-ResponseFile`.
- **`-Message` format:** single-quoted, embedded `'` doubled, one line of at most 200 chars,
  no `$` and no backtick.
- **Never edit** `.kiro/test-project/Harness/**`, `.kiro/test-project/Reports/**` or
  the Reference source (status.json `paths.reference`, appsettings `ReferenceSourcePath`).
- **No confirmation questions** at all; this step has no gate.
- **Auto-fix** selector, timing and build errors up to 3 times. Then record the evidence and
  continue.
- **Chat renders:** at the render milestone run
  `.\.kiro\test-project\lifecycle.ps1 -Action render -Page <page> -View fix-round` and send ONE
  `send_message` severity `info`: line 1 exactly `CHAT-RENDER <the absolute path printed by render>`,
  line 2 the printed summary. If render exits 1, run it once more; if it fails again, send the
  message without a CHAT-RENDER line, quoting the `[render] FAIL` line.
- **Messaging:** No info messages other than CHAT-RENDER relays. The step ends with exactly
  one `send_message` severity `success`, one line. No `warning` messages.
- **Shell timeouts:** at least 150000 ms for every `wait` call and at least 600000 ms for
  `run-page` without `-Detach`, `rerun-target` and `report`.

## Method

Follow `#phase-4-fix-rerun.md`, `#reporting-standards.md` (investigation order: selector ->
data -> app) and `#css-guide.md` for styling fixes.

1. Start (every iteration): `.\.kiro\test-project\lifecycle.ps1 -Action progress -Page <page> -Phase 3 -Message 'Fix step started'`.
2. Read `status.json`. If `fixLoop.clean == true`, end with success "Clean; nothing to fix."
3. **Fixable modules** = modules with `openFindings > 0` or `actionableCssDiffs > 0` (actionable
   page-level CSS groups the module owns). Modules
   with `harnessError != null` and no open items are skipped. If there are none, end with
   success "No fixable findings (harness errors only)" **without** calling `fix-round`.
4. Run `.\.kiro\test-project\lifecycle.ps1 -Action fix-round -Page <page>` once. If it prints
   `budget-exhausted`, end with success
   "Fix budget (2 rounds) used; remaining findings go to the final report."
   Otherwise note the printed `round=<n>`.
5. For **every** fixable module X, in this one step: read `runs/Module<X>/summary.json`
   (findings with `kind`, `missingAssertions`, `missingCaptures`) and the results with file
   tools, plus the page-level CSS groups in `runs/css_groups.json` (rule 5); apply the rules below; append every edit to `fixes.json`. After Target
   test edits run `.\.kiro\test-project\lifecycle.ps1 -Action build-module -Page <page> -Module <X>`.
6. Run `.\.kiro\test-project\lifecycle.ps1 -Action rerun-target -Page <page> -Modules <affected, comma-separated>`
   once (affected = modules with edits plus every module in the `modules` (Seen in) list of each CSS
   group you fixed) and handle the exit code by the fix-step table. A retry reruns only the failed
   modules.
7. `render -View fix-round`, then the CHAT-RENDER info message.
8. End with exactly one `send_message` success, e.g.
   "Round 1: A 20->0, C 2->1 findings. Next: re-check." (before/after from the
   `[fix-round] round n Module X: findings a -> b, CSS c -> d` console lines or
   `fixLoop.history`).

## Target-fixer rules

1. Read `runs/Module<X>/summary.json` and the results with file tools. Each finding comes with
   its `kind` already set by the generator.
2. **`test-selector`:** edit only selectors, waits and helper calls in
   `.kiro/test-project/Modules/<page>.Mod<X>/<page>Module<X>_v2.cs` (the Target test, never
   `Reference*`). Never change expected constants, and never remove or loosen assertions.
3. **`test-expectation`:** set the Target EQUALS/CONTAINS expected constant to the Reference
   EQUALS/CONTAINS constant with the same `(step, field, type)`, copied verbatim. That is the
   only allowed constant change. CHANGES and STAYS SAME are never edited under this rule.
4. **`test-coverage`** (step or assertion), and the `missingAssertions` and `missingCaptures`
   of **any** finding:
   - Missing step: add it to the Target test, mirroring the Reference step.
   - Missing assertion: add the mirrored assertion to the existing Target step, at the
     Reference position.
   - Missing capture: add the mirrored `runner.Capture(state, field, <Target value>)` in the
     same Target step, at the same position as in the Reference test body (this includes the
     NAV/PRE steps), where `<Target value>` is the value the Target test reads at that point.
   - EQUALS/CONTAINS constants are copied verbatim; CHANGES/STAYS SAME take their "before"
     from Target runtime state. Adding assertions never loosens or removes existing ones.
   - On a finding of kind `app` or `test-selector`, the missing assertions and captures are
     added in the same round as the main fix.
5. **`app`** (and actionable CSS groups): edit files under the Target source (status.json `paths.target`, appsettings `TargetSourcePath`) only (the Target app).
   - **CSS work comes as page-level groups** (`runs/css_groups.json` `groups[]`, also
     `reports/CSS_Groups.md`). Work on the groups with `actionable: true` whose `owner` is a fixable
     module: **one task per group**, titled with the group's plain-English `issue` sentence (e.g.
     `The Payor Name column cells (first 3 rows) exist on the old page (Reference) but not on the new
     page (Target) while a cell is being edited.`); `action` is the technical instruction. Groups with
     `referenceWorse: true` (only the Reference overflows/clips at a width) are never Target work;
     `alsoIn` lists the hover/focus/disabled states the same values also apply to (fix them in the
     same rule). A `theme` group (same role class, property and Reference/Target
     values on many elements, e.g. `Buttons: border-radius 4px -> 3px, 46 elements`) is fixed with
     ONE CSS rule or theme token change in the Target source (TargetSourcePath), never per element.
     `individual` groups (missing/extra elements, `layout.*`, `responsive.*`, geometry) follow the
     per-row rules below. Use `findingIds` `["css:group:<key>"]` (`key` from css_groups.json, stable
     across re-runs). Rerun every module in the group's `modules` list, not only the owner. A group
     whose `action` starts with `Reference larger`, `accepted: true` groups and Reference defects never
     cause Target edits or padding. The raw rows (`runs/css_rows_all.json`, `summary.json cssDiffs`) are
     the audit: use them only to find the elements of a group (`groupKey`).
   Never edit the Reference source (status.json `paths.reference`, appsettings `ReferenceSourcePath`). The runner rebuilds and restarts the Target when its
   sources are newer than the running process.
   - **Geometry rows** (CSS diff property `layout.*`, e.g. `layout.gapToPager`, `layout.rowHeight`,
     `layout.height`, `layout.headerHeight`, `layout.gapToGrid`): Target layout fixes, such as a
     fixed or minimum grid height that leaves empty space, row/cell heights, padding or margins,
     pager placement. When the Action starts with `Fix Target layout`, aim for the Reference value
     within the tolerance (4px / 10%). When it starts with `Reference larger`, the Reference has the
     bigger gap or height and may own the defect: never add empty space or height to the Target to
     match it; leave the row open (it stays counted) and report it so the user decides (accept in
     css-map `accepted`, or treat it as a Reference issue). Use the fixer
     key `css:{Element}.{Property}` (e.g. `css:Grid pager.layout.gapToPager`) in `findingIds`.
     Never add a css-map `accepted` entry yourself.
   - **Unmeasured geometry** (property `(unmeasured)`: a role element's Reference selector matched
     nothing; `(unmeasured: needs role <r>)`: a pager/filter element has no `row`/`grid` element in
     that state): fix the selector or add the role element in `state-map/css-map.json`, never the
     Reference source. These rows are counted so a bad selector can never hide a layout gap.
   - **Automatic inventory rows** (`summary.json cssDiffs[].layer` = `match`, `style`, `state`,
     `layout`, `responsive`): read `cssDiffs[].action` (the same text as the CSS report's Action
     column) and act only on the Target. `(missing on Target)`: render the element on the Target with
     the Reference role and accessible name (button text, label, alt). `(extra on Target)`: LOW rows
     are not actionable; MEDIUM (interactive) rows: confirm the extra control is intended, remove it
     only when it is clearly a leftover. `text.*`: align the label/placeholder/name text. `style` /
     `state` (`hover.*`, `focus.*`, `disabled.*`): set the Reference value in the Target CSS (for
     states, the `:hover` / `:focus` / `:disabled` rule). `layout.*` / `responsive.<w>.*`: fix the
     Target layout (order, alignment, wrapping, overflow, overlap). Any px row whose Action starts
     with `Reference larger` follows the geometry rule above: never add padding, height or empty space
     to the Target. Rows with `accepted: true` (`acceptedBy` = `profile <name>` or `css-map`) are
     never fixed. Never edit `profiles/*.json` or add acceptances yourself.
   - **Content findings** (kind `app`, fields `page.leakedSyntax`, `page.consoleError`,
     `page.pageError`, `page.brokenImage`, `page.failedRequest`, `page.emptySection`, `page.a11y`,
     source `content-scan`): the Target shows server/template syntax, raises browser errors, has a
     broken image or failing request (4xx/5xx), an empty section or a control/image without an
     accessible name; fix the Target page so the text renders (or the error is gone, the image/request
     loads, the control gets a label/aria-label/alt).
   - **Reference defects** (`summary.json referenceDefects`, ids `<X>-REF<n>`) are baseline issues:
     never fix, never count and never touch them (and never edit the Reference source for them).
6. Rebuild after any test edit with
   `.\.kiro\test-project\lifecycle.ps1 -Action build-module -Page <page> -Module <X>`.
7. Record every edit in `.kiro/app-testing/<page>/fixes.json` (a JSON array; append):
   `{ "round": n, "source": "loop", "module": "X", "findingIds": ["..."], "kind": "app", "file": "<TargetSourcePath>/...", "change": "one line", "before": "<exact old snippet>", "after": "<exact new snippet>", "at": "<ISO time>" }`.
   `before` is what the exit-3 revert restores.

Never edit Reference tests, never weaken an assertion, never mark anything PASS yourself:
the verdict comes only from the runner's re-run.

## Fix-step exit-code table (`rerun-target -Modules`; never pause)

| Exit | Action |
|---|---|
| 0 | Continue |
| 1 + `error CS` in `logs/target-build.log` | Fix the app compile error (counts toward the 3 attempts this round), then re-run |
| 1 + `error CS` in `logs/build-<X>.log` | Fix the Target test compile error (same 3-attempt budget), `build-module -Module <X>`, re-run |
| 1, other | Retry once (failed modules only). Then `.\.kiro\test-project\lifecycle.ps1 -Action progress -Page <page> -Message 'HARNESS-ERROR X: <reason>'` and continue |
| 2 | Edit nothing. For each module named, `.\.kiro\test-project\lifecycle.ps1 -Action progress -Page <page> -Message 'HARNESS-ERROR X: reference baseline unavailable'` and continue (the final report shows it UNRESOLVED) |
| 3 | If `logs/target.err.log` shows the Target app failed to start because of this round's edit for a module, revert that edit with file tools (using `before` from fixes.json) and re-run once. Otherwise retry once. Then record HARNESS-ERROR as above and continue |

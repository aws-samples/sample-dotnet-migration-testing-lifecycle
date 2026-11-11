---
name: test-author
description: "Steps 'author-1'..'author-6' of the test-page workflow (Phase 1, parallel slots). Checks the plan is signed off, then writes or updates the Reference and Target Playwright/NUnit test classes for the slot's module(s) in Modules/{Page}.Mod{X}/ (mirroring every Reference assertion and capture on Target), and builds each module with lifecycle.ps1 build-module until it compiles. No human gate."
tools: ["read", "write", "shell"]
---

# Test Author (workflow steps `author-1` .. `author-6`, Phase 1)

> Phase numbers in this file are the runner's `-Phase` values (Phase 1 shows as "Phase 2 of
> 5"). Mapping: [Phase numbering](../workflows/README.md#phase-numbering).

You turn the signed-off plan into compiling Reference and Target tests for the modules of
**your slot**. Six author steps run in parallel inside the workflow node `author`; each has
its own module project, so the slots never share files or build output. You do not record
the plan sign-off (the plan step did) and you do not run tests.

`<page>` below is the workflow input `page`. The page dir is `.kiro/app-testing/<page>/`.
All paths are relative to the repo root, which is the Kiro workspace.

## Common rules (every testing agent)

- **Shell:** only `.\.kiro\test-project\lifecycle.ps1 -Action ...`. No `dotnet` and no inline
  scripts (no sleep/poll loops, `Test-NetConnection`, `Get-Content` or `git`). Builds go
  through `-Action build-module`.
- **State:** read it with file tools only (status.json, plan.json, build.json, build logs).
  `lifecycle.ps1` is the only writer of status.json, timings.json, PROGRESS.md and `chat/`.
- **Human text never goes on a command line.** Write it to
  `.kiro/app-testing/<page>/gate-response.txt` with a file tool and pass `-ResponseFile`.
- **`-Message` format:** single-quoted, embedded `'` doubled, one line of at most 200 chars,
  no `$` and no backtick.
- **Never edit** `.kiro/test-project/Harness/**`, `.kiro/test-project/Reports/**` or
  the Reference source (status.json `paths.reference`, appsettings `ReferenceSourcePath`).
- **No confirmation questions** outside the designed gates.
- **Auto-fix** selector, timing and build errors up to 3 times. Then record the evidence and
  continue.
- **Chat renders:** at each render milestone run `-Action render` and send ONE `send_message`
  whose line 1 is `CHAT-RENDER <the absolute path printed by render>` and line 2 the printed
  summary. This step has no render milestone, so in practice it sends none.
- **Messaging:** No info messages other than CHAT-RENDER relays. The step ends with exactly
  one `send_message` severity `success`. When the plan is not signed off it sends `warning`
  instead (the slot pauses) and never ends with success or error for that reason.
- **No progress calls** in this step (six slots run at once; this avoids page-lock
  contention).
- **Shell timeouts:** at least 150000 ms for every `wait` call and at least 600000 ms for
  `run-page` without `-Detach`, `rerun-target` and `report`.

## Slot role

The prompt gives your slot number `k` (1..6).

| Slot | Modules it authors |
|---|---|
| 1..5 | only `plan.json.modules[k-1]` |
| 6 | `plan.json.modules[5]` and every later module, one after another |

If `plan.json.modules` has fewer than `k` entries, end immediately with `send_message`
success `slot k unused`, with no shell call. Never touch another module's files.

## Method

Follow `#module-test-standards.md`, `#phase-1-reference-baseline.md` (Reference tests) and
`#phase-2-target-capture.md` (Target tests). Use the existing
`.kiro/test-project/Modules/*/*_v2.cs` files as the pattern (`ModuleTestRunner`,
`ExecuteStep`, `AssertEquals` / `AssertChanged` / `AssertUnchanged` / `Assert`,
`Capture(stateId, field, value)`,
`CssCompareHelper.DiscoverAndCaptureAll(Page, "reference"|"target", state)` at every planned
CSS state, `GenerateReport()`).

1. Read `status.json` and `state-map/plan.json` with file tools. If `plan.signedOff != true`
   or `plan.signedOffRevision != plan.json.revision`: send `send_message` warning
   "waiting for plan sign-off (status.json plan.signedOff is false)", write nothing, and end
   your turn. The warning pauses this slot, so the parallel `author` node (join all) cannot
   finish. When you are resumed, start again at step 1.
2. Apply the slot rule above.
3. For each module X of the slot, read `state-map/transitions.md`, `assertions.md`,
   `css-capture-plan.md` and the plan entry of X (`steps[]`, `cssStates`, `dataEffect`, plus
   the top-level `states[]`). Write ONLY:
   - `.kiro/test-project/Modules/<page>.Mod<X>/Reference<page>Module<X>_v2.cs` with test
     method `plan.json referenceTest` (e.g. `Reference<page>_ModX`);
   - `.kiro/test-project/Modules/<page>.Mod<X>/<page>Module<X>_v2.cs` with test method
     `plan.json targetTest` (e.g. `<page>_ModX`).
   Step names are the plan `steps[].id` values. Skip writing only when both files exist, both
   headers equal `status.json plan.fingerprintRevision` (== `plan.json.revision`; the runner
   bumps it when the plan structure changed) and both headers' `steps=<k>` equal the module's
   `plannedSteps`.
4. **Resume / plan-revision header.** Line 1 of every test file is
   `// plan-revision: <n>; steps=<k>` (`<k>` = the module's `plannedSteps`).
   - A file that exists, whose header revision equals `status.json plan.fingerprintRevision`
     (== `plan.json.revision`), whose header `steps=` equals `plannedSteps`, and that builds is
     left untouched.
   - A file whose header revision differs from `plan.fingerprintRevision`, or whose `steps=`
     differs from `plannedSteps`, must be updated (below) even if it builds.
   - **Update** = edit only the steps whose plan entry changed, then set the header to the new
     revision. Never regenerate an existing file wholesale.
   - A Reference test file whose module is unchanged (same `id`, `plannedSteps`, test names
     and `cssStates` as in status.json) is never edited; only its header is bumped.
   - New files get the header on line 1.
5. **Authoring invariants:**
   - Target step names are identical to the Reference step names.
   - Every Reference assertion is mirrored on Target with the same `(step, field, type)` and
     in the same order.
   - Every Reference `Capture(state, field)` is mirrored on Target in the same step, with the
     same `state` and `field`; the value is the Target's runtime value.
   - Only **EQUALS/CONTAINS** expected constants are copied verbatim from the Reference
     assertion with the same `(step, field, type)`. CHANGES and STAYS SAME are mirrored as
     assertions whose "before" comes from Target runtime state (a value the Target test read
     earlier), never as a copied constant. BOOL has no constant to copy.
   - `assertions.md` chooses fields, never values. Expected values come from the Reference
     system (the Reference test reads them at runtime or they are the Reference baseline).
   - Never weaken an assertion (no "non-empty" or CONTAINS where the Reference has EQUALS).
6. **Build:** `.\.kiro\test-project\lifecycle.ps1 -Action build-module -Page <page> -Module <X>`.
   It creates `Modules/<page>.Mod<X>/<page>.Mod<X>.csproj` when missing, builds the shared
   Harness once (mutex-protected) and then only this module. Fix compile errors
   (`.kiro/app-testing/<page>/logs/build-<X>.log`) and rebuild until exit 0, at most 3 fix
   attempts per distinct error.
   - Exit 0 with `[build-module] WARN ... not authored yet` or `Tgt not authored` means a test
     file is missing: write it and build again.
   - The step count in the `[build-module]` line is a static count of distinct step names; it
     may differ from `plannedSteps` (e.g. an extra `NAV` failure path). That is not an error.
7. End with ONE `send_message` success, one line per handled module:
   - `Module X: Ref 13 steps/54 assertion calls, Tgt 13/38, build PASS (rev 1)` (numbers from
     the `[build-module] X PASS ... (Ref <steps> steps/<calls> assertion calls, Tgt <steps>/<calls>, rev <n>)`
     line), or
   - `Module X: unchanged (rev 1), build PASS`, or
   - after 3 failed attempts, `Module X: build FAIL: <first error CS line>`. This is still a
     success, so the other slots and the run continue; `run-page` reports the module as
     NOT_RUN.

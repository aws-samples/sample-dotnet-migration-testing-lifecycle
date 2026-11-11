---
inclusion: manual
---

# Phase 0: Analyze Reference Code — Execution Steps

Reference: `#phase-0-analysis.md`
Used by: Testing Lifecycle Agent when starting a fresh page.
For module design principles: `#state-machine-testing.md`

---

## Purpose

Analyze the reference source code to produce a complete state-transition map organized as user workflows (modules). This is the foundation for ALL testing.

---

## Step 0.1: Read Source Code

Read ALL relevant source files for the page:
1. The reference implementation (source of truth for expected behavior)
2. Dependent controls/components referenced by the page
3. Client-side logic (JavaScript, event handlers)
4. The target implementation (to understand selectors and current state)

Identify from the source:
- All controls (inputs, buttons, dropdowns, checkboxes, grids)
- All event handlers (click, change, submit)
- All conditional rendering (authorization, data-dependent visibility)
- All state management (session, URL params, hidden fields)

---

## Step 0.2: Identify ALL States

A state = a stable page configuration the user sees at rest.

Common states for data-entry pages:

| State ID | Name | How to Reach | Key Indicators |
|----------|------|-------------|----------------|
| S0 | Clean (page load) | Navigate to page | Default values, save indicator clean |
| S1 | Dirty | Change any field | Save indicator active, changed values |
| S2 | Dialog Open | Click save/action | Modal visible, overlay |
| S3 | After Save | Complete save flow | Fresh data, success message, clean |
| S4 | After Cancel | Cancel from dirty | Reverted to server state, clean |
| S5 | After Upload/Add | Add new item | Item appears, dirty |
| S6 | After Delete | Remove item | Item gone, may be dirty |
| S7 | Sorted | Click column header | Different row order |
| S8 | Navigation Blocked | Navigate while dirty | Confirm dialog visible |
| S9 | View Mode | Read-only privilege | Edit controls hidden/disabled |

Document each state with: ID, Name, How to Reach, Key Indicators.

---

## Step 0.3: Design Modules as Workflows

**Follow module design rules from `#state-machine-testing.md`.**
**For grid pages: Follow `#pagination-sorting-standards.md` for Module A.**

Key rules:
- Each module = one complete user workflow
- Steps are sequential (step N depends on step N-1)
- Sub-flows = edge cases and validations
- NEVER group by action type — group by user goal
- Module A (Page Load & Grid Inspection) covers pagination + sorting

**Mandatory format:**

```markdown
## Module X: [Action] → [Outcome]

*"[First-person story]"*

| Step | What the user does | What should happen |
|---|---|---|
| 1 | [action] | [outcome] |

**Sub-flow X1: [Edge Case]**
| Step | What the user does | What should happen |
|---|---|---|
| 1 | [action] | [outcome] |
```

---

## Step 0.4: Define Assertions Per Step

For each step, document:
- **CHANGES** — fields that MUST differ after the action
- **STAYS SAME** — fields that MUST NOT change

```markdown
| # | Field | Before | After | Type |
|---|-------|--------|-------|------|
| 1 | saveButton.state | "clean" | "dirty" | CHANGES |
| 2 | grid.rowCount | N | N | STAYS SAME |
```

---

## Step 0.4a: Standard Grid Behaviors (MANDATORY for paginated grids)

Module A MUST include ALL of these:

### Pagination (6 scenarios)

| # | Scenario | Action | Expected |
|---|----------|--------|----------|
| 1 | Next page | Click next | Pager advances, rows change |
| 2 | Previous page | Click prev | Returns to previous |
| 3 | Last page | Click last | Jumps to final page |
| 4 | First page | Click first | Returns to page 1 |
| 5 | Page size change | Change dropdown | Row count changes, pager recalculates |
| 6 | Direct page input | Type number + Enter | Jumps to that page |

**Always capture pager info text** (e.g., "View 1 - 10 of 965") as baseline.

### Sort (per sortable column)

| # | Scenario | Action | Expected |
|---|----------|--------|----------|
| 1 | Sort ASC | Click header once | Indicator appears, order changes |
| 2 | Sort DESC | Click same header again | Indicator toggles, order reverses |

**Sort assertion strategy:** Compare full column sequence before/after. NEVER rely solely on one row of a different column.

---

## Step 0.5: Define CSS Capture States

| State | When | Key Elements |
|-------|------|--------------|
| page_load | After initial load | Headers, rows, buttons, info |
| dirty | After making a change | Dirty indicator, changed controls |
| dialog_open | Modal visible | Dialog container, buttons, overlay |
| ... | ... | ... |

Include transient elements (dialogs, popups, expanded dropdowns).

---

## Step 0.6: Write plan.json + css-map.json, Present (GATE 1: plan sign-off)

This is the first of the two designed human gates. In the workflow it is owned by the
`test-planner` agent (step `plan`).

1. **Write `state-map/plan.json`** (the runner reads it for module order, test names,
   planned steps, CSS states and the parallel schedule; the `plan` chat render shows it as
   tables):

   ```json
   { "page": "PayorList", "revision": 1,
     "states": [ { "id": "S0", "name": "Clean (page load)", "howReached": "Navigate to the page",
                   "indicators": ["grid page 1 with 10 rows", "pager 'View 1 - 10 of 15'"] } ],
     "modules": [ { "id": "A", "name": "...",
                    "userStory": "I open Payor Information, flip through the pages and sort columns",
                    "dataEffect": "read-only", "plannedSteps": 13,
                    "referenceTest": "ReferencePayorList_ModA", "targetTest": "PayorList_ModA",
                    "cssStates": ["page_load", "sorted"],
                    "steps": [ { "id": "Step1", "action": "Load the page and verify the initial grid (S0)",
                                 "changes": [], "staysSame": [],
                                 "checks": ["grid.rowCount 10", "pager.info 'View 1 - 10 of 15'"] } ],
                    "notTested": [ { "item": "...", "reason": "..." } ] } ] }
   ```

   **Field contract:**

   | Field | Required | Rule |
   |---|---|---|
   | `page`, `revision` | yes | revision per the rule below |
   | `states[]` | yes | `{id, name, howReached, indicators[]}` for every state of Step 0.2 |
   | `modules[].id`, `name`, `referenceTest`, `targetTest` | yes | tests `Reference<Page>_Mod<X>` / `<Page>_Mod<X>` |
   | `modules[].userStory` | yes | first-person story of the workflow |
   | `modules[].dataEffect` | yes | `mutating` when any step saves/persists; `read-only` for Cancel-only, filter, sort or paging modules unless something persists. Missing = treated as `mutating` |
   | `modules[].plannedSteps` | yes | number of runtime steps |
   | `modules[].steps[]` | yes | `{id, action, changes[], staysSame[], checks[], checksText[]}`; `steps.length == plannedSteps`; ids are the test step names; `checksText` = one plain-English sentence per check with no data values (the plan view shows only this) |
   | `fieldLabels` | yes | top-level map field pattern → plain-English label (`{n}` digits, `{name}` text), e.g. `"row[{n}].name.text": "Payor Name in row ID {n}"`; used by the module results and reports |
   | `modules[].cssStates` | yes | every state has elements in css-map.json |
   | `modules[].notTested[]` | yes (may be empty) | `{item, reason}` with a specific reason |

   `dataEffect` drives the run: read-only modules run in parallel; mutating modules run one
   at a time with a service restart after each system. Prefer at most 6 modules (the workflow
   has 6 parallel author slots; slot 6 writes modules 6..N one after another).

   **Revision rule.** When `plan.json` exists, start from it. A new plan starts at
   `revision: 1`. Bump `revision` **only** when some module's `id`, `plannedSteps`, test names
   or `cssStates` change (or a module is added/removed). Wording-only edits to
   `state-map/*.md`, `notTested` or `name`, and adding `userStory`, `dataEffect`, `steps`,
   `states`, `checksText` or `fieldLabels`, do not bump it. A new revision resets the changed modules to NOT_RUN and starts
   a new run session; an unchanged revision keeps run data.

2. **Write `state-map/css-map.json`**: per planned state, the elements to compare, each with
   a Reference selector, a Target selector, the properties and their severity, and optional
   `accepted` reasons. Target selectors must not depend on classes a fix would add
   (e.g. `.mud-table-head th.mud-table-cell`, `button:has-text("Search")`).

3. **Record the gate:**
   `.\.kiro\test-project\lifecycle.ps1 -Action gate -Page <PageName> -Gate plan_signoff -State pending`

4. **Present:** `.\.kiro\test-project\lifecycle.ps1 -Action render -Page <PageName> -View plan`
   prints one `CHAT-RENDER <path>` line per part (overview, then one per module) and a summary (e.g. "Plan revision 1: 4 modules (A, B, D
   read-only; C mutating), 35 steps, 128 planned checks."). The rendered file has the module
   table, a steps table per module, the states and the NOT_TESTED items; it is pasted into the
   chat verbatim (`#lifecycle-chat-display.md`). Ask "Reply confirm to sign off, or describe
   changes." and wait for the reply.

5. **Reply handling:**
   - `confirm` / `approve` / `yes` / `ok` (whole reply): write it to
     `.kiro/app-testing/<PageName>/gate-response.txt` with a file tool, then
     `.\.kiro\test-project\lifecycle.ps1 -Action gate -Page <PageName> -Gate plan_signoff -State confirmed -ResponseFile .kiro/app-testing/<PageName>/gate-response.txt`.
   - Anything else is revision feedback: first
     `.\.kiro\test-project\lifecycle.ps1 -Action progress -Page <PageName> -Phase 0 -Message 'Revising plan (feedback received)'`
     (closes the gate wait and reopens phase 0), then update the plan files (revision rule),
     re-run `gate ... -State pending`, render and re-present (**revision loop**).
   - Resume: if the plan is already signed off at the current `revision`, do not re-plan;
     ask "reply confirm to resume" (re-confirming the same revision is a runner no-op).

---

## Output Files

Create in `.kiro/app-testing/{PageName}/state-map/`:
1. `states.md` — state definitions
2. `transitions.md` — modules with workflow tables
3. `assertions.md` — field-level changes per step
4. `css-capture-plan.md` — CSS states and elements
5. `plan.json` — states, modules (user story, data effect, steps, test names, planned steps, CSS states, NOT_TESTED), `revision`
6. `css-map.json` — Reference/Target selector pairs per CSS state

`status.json` is written only by `lifecycle.ps1` (the `gate` call imports `plan.json`).

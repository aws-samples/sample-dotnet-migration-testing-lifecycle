---
inclusion: manual
---

# CSS Comparison Methodology

Reference: `#css-comparison-methodology.md`

How to capture, compare, and report CSS differences between reference and target systems.

---

## Capture Strategy

### Auto-Discovery (Default)

At every state transition, `CssCompareHelper.DiscoverAndCaptureAll()`:
1. Walks the entire visible DOM
2. Captures computed styles for every element
3. Records: font-family, font-size, font-weight, color, background-color, border, padding, margin, text-align, display, width, height

### When to Capture

| State | Trigger | Elements |
|-------|---------|----------|
| page_load | After initial load | All visible elements |
| dirty_state | After user makes a change | Dirty indicator, changed controls |
| dialog_open | When modal is visible | Dialog content, buttons, overlay |
| grid_edit | When row is in edit mode | Editable cells, controls |
| sorted | After sort applied | Sort indicators, row highlights |

**Include transient elements** — dialogs, popups, expanded dropdowns must be captured WHILE VISIBLE.

### Geometry (layout.*)

Typographic properties alone miss layout breaks (e.g. empty space between the last grid row and the
pager). Every mapped element in `css_mapped_<state>.json` also carries its bounding box in page
coordinates: `layout.x`, `layout.y`, `layout.width`, `layout.height` (first visible match),
`layout.visibleCount` and `layout.lastBottom` (bottom of the lowest visible match).

Derived metrics come from the optional css-map `role` per element (first element with that role in the state):

| Role | Metric | Definition |
|------|--------|------------|
| `grid` | `layout.height` | grid container height (header + body, without the pager) |
| `row` | `layout.rowHeight` | first visible data row height |
| `header` | `layout.headerHeight` | header row height |
| `pager` | `layout.gapToPager` | top of the pager's visible content (`layout.contentTop`: first non-blank text, control or image inside it; else the box top) − last visible row bottom (needs a `row` element). Empty space inside a tall pager container counts as gap |
| `filter` | `layout.gapToGrid` | grid top − filter panel bottom (needs a `grid` element) |

Geometry-only elements use `"properties": {}`. Raw `layout.width` / `layout.height` of any element are
compared only when listed in its `properties`. Tolerance (appsettings `VisualChecks.Geometry`): a row
is flagged when |diff| > 4px AND > 10%; HIGH when > 40px OR > 50%, else MEDIUM. To accept an
intentional difference, add `"accepted": { "layout.rowHeight": "<reason>" }` on that element; an
accepted metric never hides another one (accepting the row height does not accept the pager gap).

When the Reference value is the larger one, the row's Action reads `Reference larger: ...`: the
baseline may own the defect, so the Target is never padded to match (the row stays counted until the
user accepts it). A role element whose Reference selector matches nothing gives a counted
`(unmeasured)` row, and a pager/filter element without a `row`/`grid` element in its state gives
`(unmeasured: needs role <r>)`, so a selector problem can never hide a layout gap. `(unmeasured)` rows
ignore `accepted`: when a page legitimately has no such element (e.g. no pager), remove that
element's `role` (or the element) from css-map.json instead.

---

## Comparison Process

### Step 1: Collect Captures

After all modules complete for both systems:
- Reference captures: `snapshots/Reference/Module*/`
- Target captures: `snapshots/Target/Module*/`

### Step 2: Match Elements

Find elements present in BOTH captures (by selector/identifier).

### Step 3: Compare Properties

For each matched element, compare key CSS properties:
- font-family, font-size, font-weight
- color, background-color
- border (width, style, color)
- padding, margin
- text-align, display
- width, height

### Step 4: Classify Differences

| Category | Criteria | Action |
|----------|----------|--------|
| 🔴 Real (Must Fix) | font-weight, color, background, visibility, display differences affecting appearance | Fix before reports |
| 🟡 Structural | height/width >5px, display type (table vs block) inherent to framework | Document as acceptable |
| 🟢 Cosmetic | ±1px, font-family fallback list, line-height equivalent, sub-pixel rounding | Auto-accept |

### Auto-Accept Rules (Not Real Differences)

- ±1px on any dimension
- Geometry (`layout.*`) within tolerance (|diff| ≤ 4px or ≤ 10%)
- `rgb(0,0,0)` vs `#000000` (same color, different format)
- Font-family list differences where primary font matches
- `start` vs `left` for text-align
- `normal` vs `400` for font-weight
- Platform-specific font rendering

---

## Automatic Inventory (any web app to web app)

The css-map rows above only cover what the planner listed. At every planned CSS state, on both
sides, `CssCompareHelper` also writes `runs/Module<X>/<System>/css/inventory_<state>.json`
(`ElementInventory`), and the report step (`AutoCssComparer`, browser-free, deterministic) turns
the two inventories into extra CSS diff rows. Nothing here is page- or stack-specific: stack
knowledge lives in profiles.

### Discovery

Visible (box > 0, not hidden) buttons, links, inputs, selects, textareas, ARIA widgets, headings,
labels, table headers, cells of the first `Inventory.MaxRowsPerGrid` data rows (default 3, repeated
rows are grouped), images and alert/status/validation messages, capped at `Inventory.MaxElements`
(default 300) in DOM order. Per element: role (implicit ARIA role or `role` attribute), accessible
name (in-page approximation of accname: `aria-labelledby` > `aria-label` > label > `alt` > content >
`title` > `placeholder`), label, placeholder, input type, text, page box, DOM path, container
(nearest ancestor with ≥ 2 inventoried elements) and order in it, grid row/column, interactive,
disabled, normalized styles, overflow/ellipsis, line count, clipping. The first visible match of
each css-map element is tagged so the inventory records its css-map name.

### Matching (`runs/Module<X>/match_<state>.json`)

1. css-map pairs first (same css-map element name on both sides): explicit, always win.
2. Greedy best score for the rest, highest first, ties by Reference DOM order. Score: role equal or
   equivalent in the stack profile is required; accessible name/label/text equal 0.6 (token Jaccard
   ≥ 0.6 → 0.6 × similarity; both empty 0.3); same order in the container (grid cells: same row and
   column) 0.25 (off by one 0.12); geometry proximity 0.15 × (1 − distance/400px). Pair when ≥
   `Inventory.MinMatchScore` (default 0.6). Roles in the profile `ignoreRoles` are skipped.
3. Unpaired Reference element → `(missing on Target)` HIGH (interactive or not); unpaired Target
   element → `(extra on Target)` LOW (MEDIUM if interactive). Label/placeholder/name text
   differences on a pair → `text.label|text.placeholder|text.name` MEDIUM (LOW for case/punctuation).

### Normalization (`StyleNormalizer`)

Colors → `rgba(r,g,b,a)` (hex/rgb/named, alpha-aware, alpha 0 = transparent); px rounded to 0.5px;
`normal` = 400, `bold` = 700, `start` = left; borders compared per side (no border = width 0, style
none or transparent color, whatever the color); `background-color` compared as the effective
background (first non-transparent ancestor background); `font-family` compared as the first family
that actually renders (`document.fonts.check` + width probe), not the whole stack.
`VisualChecks.IgnoreProperties` (default `cursor`) are skipped on non-interactive elements.

### Rows by layer (`cssDiffs[].layer`)

| Layer | Property | Severity |
|-------|----------|----------|
| mapped / geometry | css-map properties / `layout.height`, `layout.rowHeight`, `layout.headerHeight`, `layout.gapToPager`, `layout.gapToGrid` | as above |
| match | `(missing on Target)`, `(extra on Target)`, `text.*`, `(layout text not found on Target)` | see Matching; layout text LOW (not counted) |
| style | computed styles of auto pairs (css-map pairs already have mapped rows) | `DefaultCssSeverity` |
| state | `hover.*`, `focus.*`, `disabled.*` (first `InteractiveStates.MaxElements` = 12 interactive elements, states in `InteractiveStates.States` = page_load). Chromium forces `:hover` / `:focus` / `:focus-visible` via CDP `CSS.forcePseudoState` (no events reach the app); without CDP: real hover, focus only on non-text-entry controls | color/background HIGH, else MEDIUM |
| layout | `layout.order`, `layout.overflow`, `layout.lines`, `layout.overlap`, `layout.hidden` | HIGH |
| layout | `layout.align`, `layout.labelPosition`, `layout.spacing` (Geometry tolerance + `Layout.SpacingMinDiffPx`) | MEDIUM |
| responsive | `responsive.<w>.overflow`, `.lines`, `.hidden` per `Responsive.Widths` (default 1366, 768; states in `Responsive.States`), `(page)` horizontal page scroll on one side only | HIGH |

Grid cells with the same difference collapse into one row (`cell cells, columns 1, 3 (first 3 rows)`).
A px layout row where the Reference is larger reads `Reference larger: ...` like geometry rows; so does
an overflow / hidden / page-scroll row where only the Reference has the problem (Target `fits` /
`fully visible`): the Target never reproduces a Reference defect.
`overflow` counts only child content past the padding box (clipped decoration such as a transformed
ripple pseudo-element is not an overflow). A descendant with the same role and text as its nearest
inventoried ancestor (e.g. a `role=columnheader` div inside the `th`) is collapsed into the ancestor.
Limitation: forced pseudo-states apply to the element only, so styles from ancestor `:hover` or
`:focus-within` rules are not captured on either side; a missing `hover.*` / `focus.*` row is not
proof of parity for such rules.

### Stack profiles and acceptance order

`.kiro/test-project/profiles/<name>.json` (selected by appsettings `StackProfile`; `extends` chain,
`generic.json` last): `roleEquivalences`, `selectorHints` (planner hints), `accepted` (property exact
or `prefix*`, optional `role`, `element` substring, `reference`/`target` value regexes, `reason`),
`ignoreProperties`, `ignoreRoles`. Order: css-map `accepted` (per page, explicit) wins, then the
profile; the report shows `accepted by profile <name>: <reason>`. Accepted rows are never actionable.

### Severity recalibration (`CssSeverityRules`, `VisualChecks.Severity`)

Applied to `style`, `state` and `mapped` rows after they are produced and before acceptance; `match`,
`layout`, `responsive` and `geometry` rows keep their severity (missing element, order, overflow,
overlap, hidden on one side: HIGH; geometry tolerances unchanged). A severity written per property in
css-map.json is the page author's decision and is kept. Values that do not parse (`normal`, `%`,
`auto`, `inherit`) keep their severity: a rule never downgrades on missing data.

| Property (base name, also under `hover.` / `focus.` / `disabled.`) | Rule (defaults) | Severity |
|---|---|---|
| `font-size` | max diff <= `FontSizeLowPx` (1px) | LOW, else MEDIUM |
| `border-radius`, `padding*`, `margin*`, `border-*-width` | max per-side diff (shorthand expanded) <= `BoxLowPx` (1px) | LOW, else MEDIUM |
| `letter-spacing`, `line-height` | diff <= `TextMetricLowPx` (1px) | LOW, else MEDIUM |
| `font-weight` | pair in `LowFontWeightPairs` (500 vs 600) | LOW (other pairs keep their severity) |
| any `*color` | CIEDE2000 deltaE < `DeltaELow` (3) | LOW |
| any `*color` | deltaE `DeltaELow`..`DeltaEHigh` (3-10) | MEDIUM |
| `color`, `background-color` | deltaE > `DeltaEHigh` (10) on an interactive element or a `PrimaryRoles` role | HIGH (else MEDIUM; border/outline colors MEDIUM) |
| `color`, `background-color` | Target contrast < `ContrastMin` (4.5:1, WCAG AA) where the Reference passes | HIGH (wins over deltaE) |

Color math (`ColorMath`, no package): sRGB → CIE Lab (D65), CIEDE2000 per Sharma, Wu, Dalal (2005),
WCAG 2.x relative luminance and contrast ratio. Semi-transparent colors are composited over white
for deltaE and over the side's own (effective) background for contrast. Contrast is computed per side
for automatic pairs from the element's `color` and effective `background-color` (state rows use the
state values first). Every recalibrated row records its rule (`cssDiffs[].severityRule`, e.g.
`deltaE 1.8 < 3 -> LOW`), shown in `runs/css_rows_all.md`.

### Page-level groups (`CssGrouper`)

After every module's rows exist, the report groups them per **page** (all modules, all states):

- **Theme groups** (`mapped` non-geometry, `style`, `state` rows): key = (role class, property,
  `StyleNormalizer.Canonical` Reference value, canonical Target value, accepted). Role class = the
  first role (ordinal) of the profile `roleEquivalences` group containing the row's role, else the
  role; rows without a role use `el:<element>` and never merge with other elements. One theme group =
  one finding = one CSS rule / theme token change in the Target.
- **Individual findings** (`match`, `layout`, `responsive`, `geometry`, `(...)` properties): key =
  (layer, element, property, Reference, Target, accepted), so only identical repeats across states
  and modules collapse. Element-specific rows are never grouped away.
- Accepted rows never merge with open rows (nor with rows accepted for another reason). Group
  severity = the highest member severity; a group is actionable when any member row is.
- **Owner** = the first module in plan order the group was seen in. `summary.json
  actionableCssDiffs` = actionable groups owned by the module (status.json, chat and fix loop count
  these); `actionableCssRows` = raw rows (informational). Target PASS needs `actionableCssRows == 0`,
  so a module whose rows sit in another module's group never passes.
- Ids: `G1..Gn` for display (actionable first, then severity, element count, key); `key` = stable
  10-hex hash of the grouping key, used by the fixer as `css:group:<key>`.
- Outputs: `runs/css_groups.json` (fixer input), `reports/CSS_Groups.md` (every group once),
  `Module<X>_CSS_Diff.md` (groups seen in X; non-owners say `see page-level group G7 (owned by Module
  A)`), audit `runs/css_rows_all.json` / `.md` (every raw row with its group id: grouping never hides
  a difference). Chat module-result and final views show the top `Groups.ChatTop` (15) actionable
  groups plus `N more in reports/CSS_Groups.md`.
- **Border merge**: theme rows `border-top|right|bottom|left` of the same element, state and module
  with identical canonical values get the grouping property `border` (all four sides) or
  `border (top, bottom)` (a subset); sides with other values stay separate. Raw rows keep their side.
- **State fold**: a `hover.` / `focus.` / `disabled.` row whose canonical values equal the base-state
  row of the same element, state, module and (effective) property takes that row's grouping key, so it
  joins the base group; the group lists the states in `alsoIn` (Issue: `Also when hovered.`). A
  state row with other values stays its own group.
- **Reference worse**: a `responsive` row where only the Reference overflows, scrolls or is clipped
  (Target `fits` / `fully visible`) is not actionable (`ApplyAcceptance`); a group of only such rows has
  `referenceWorse: true` and is listed under "Reference worse (not counted)". Geometry and spacing px
  rows where the Reference is larger stay counted (`Reference larger: ...`, never pad the Target).
- **Issue text** (`IssueText`, deterministic templates): one sentence per group, stored as
  `groups[].issue`. Elements are named by visible name + role word; grid cells by their column header;
  colors by `ColorMath.Name` (Lab buckets: chroma < 6 is black / dark gray / gray / light gray / white
  by L*, else a hue word from the Lab hue angle with `dark` (L* < 35) / `pale` (L* > 85) prefixes,
  `brown` for dark orange/yellow, `faded <hue>` when alpha < 0.6, `transparent` at alpha 0); the WCAG
  contrast drop of the members adds `- harder to read (contrast x:1, below the 4.5:1 minimum)`.

### Column headers and layout-table cells

- **Column header resolution** (compare time, both inventories): for a grid cell row the header is the
  nearest `columnheader` of the same side above the cell whose horizontal range contains the cell's
  center (works for a header in its own table, e.g. jqGrid, and for `thead`). Stored per row as
  `cssDiffs[].columns` (`column N` when none is found); the technical `Element` text (`gridcell cells,
  column 2 (first 3 rows)`) is unchanged so group keys stay stable.
- **Layout-table cells** are left out of matching on both sides (`ElementMatcher.IsLayoutCell`): an
  implicit-role `cell` (a `td` of a table without `role=grid`; explicit `gridcell`s are always content)
  whose table has no `columnheader` / `rowheader` in the inventory AND is nested inside a table cell or
  contains a nested table. These tables only position things (WebForms/jqGrid pagers, toolbars); their
  controls (buttons, inputs, selects) stay inventoried and compared. Only cells without text of their
  own are dropped: the cell's name minus the names/labels/texts of the inventory elements inside it
  (`ElementMatcher.OwnText`; nested cells, controls, a select's option text). The dropped cells are
  listed per state in `runs/Module<X>/match_<state>.json` `excludedLayoutCells`.
- **Layout-table text** (a layout cell with its own text: pager caption `View 1 - 10 of 15`, a form
  label `Name:` in a layout table) is compared by text, not as an element: found when any element of
  the other side has a name/label/text/placeholder containing it (letters and digits only, case-
  insensitive). Not found gives a LOW, not counted row `(layout text not found on Target)` (Target
  side: `(layout text only on Target)`), element `text '<text>'`; LOW because plain text outside
  compared elements is not inventoried, so "not found" is a hint to check, not proof. Listed in
  `match_<state>.json` `layoutTextCells` (side, text, found). Module<X>_CSS_Diff.md shows both counts.

### Page health (both sides, `PageHealth.*` settings)

Broken images (loaded with naturalWidth 0) and failed requests (HTTP ≥ 400 or network failure,
same origin or image/stylesheet/script/font; requests the browser aborted itself on
postback/navigation, `net::ERR_ABORTED`, are ignored) are HIGH; empty headings/sections and accessibility
gaps (control/button without an accessible name, image without alt) are MEDIUM. Target hits are
Functional Gaps (`page.brokenImage`, `page.failedRequest`, `page.emptySection`, `page.a11y`);
Reference hits are Reference defects (never counted).

---

## Report Format

```markdown
## CSS Diff — All Modules

### Summary
| Category | Count | Status |
|----------|-------|--------|
| 🔴 Real | N | NEEDS FIX / FIXED |
| 🟡 Structural | N | DOCUMENTED |
| 🟢 Cosmetic | N | AUTO-ACCEPTED |

### 🔴 Real Issues
| # | Module | State | Control | Property | Reference | Target | Fix |
|---|--------|-------|---------|----------|-----------|--------|-----|

### 🟡 Structural
| # | Control | Property | Reference | Target | Reason |

### 🟢 Cosmetic
N items — font fallbacks, sub-pixel rounding.
```

---

## Fix-Iterate Loop

1. Apply CSS fixes to target app
2. Rebuild
3. Re-run affected modules (`.\.kiro\test-project\lifecycle.ps1 -Action rerun-target -Page <page> -Modules <X,Y>`)
4. Re-compare (the runner regenerates `reports/Module<X>_CSS_Diff.md`)
5. Repeat for at most 2 auto-fix rounds; never pause to present
6. The human approves or asks for more fixes at the final report gate

---

## Completion Criteria

- [ ] All module CSS captures compared
- [ ] All 🔴 Real issues FIXED, ACCEPTED (css-map `accepted`), or listed UNRESOLVED in the final report
- [ ] Final report signed off (gate 2)
- [ ] status.json updated by the runner

---

## DO NOT:

- Skip comparison claiming "structural differences make it impossible"
- Only compare element counts without property-level comparison
- Mark structural differences as acceptable without checking shared elements
- Report only HIGH severity and ignore MEDIUM

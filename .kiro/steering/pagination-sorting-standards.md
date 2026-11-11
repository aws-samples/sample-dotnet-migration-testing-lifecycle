---
inclusion: manual
---

# Pagination & Sorting Test Standards

Reference: `#pagination-sorting-standards.md`

Standard patterns for testing paginated grids and sortable columns. Apply to Module A (Page Load & Grid Inspection) for any page with a data grid.

---

## Pagination — 6 Mandatory Scenarios

Every paginated grid MUST test ALL 6:

| # | Scenario | Action | Expected Outcome |
|---|----------|--------|------------------|
| 1 | Next page | Click next icon | Pager info advances (e.g., "1-10" → "11-20"), rows change |
| 2 | Previous page | Click prev icon | Returns to previous page |
| 3 | Last page | Click last/end icon | Jumps to final page |
| 4 | First page | Click first icon | Returns to page 1 |
| 5 | Page size change | Change dropdown (e.g., 10→25) | Row count increases, pager recalculates |
| 6 | Direct page input | Type page number + Enter | Jumps to that page |

After testing all 6, **restore grid to page 1 with original page size**.

---

## Pager Info Validation

At EVERY pagination step, capture and validate:
- Format: "View X - Y of Z" (or equivalent)
- X = first record on page
- Y = last record on page
- Z = total records (the TRUE measure of dataset size)

**ALWAYS capture Z as baseline value.** It anchors all other assertions.

---

## Sort — Standard Pattern

For each sortable column:

| # | Scenario | Action | Expected |
|---|----------|--------|----------|
| 1 | Sort ASC | Click column header once | Sort indicator appears, row order changes |
| 2 | Sort DESC | Click same header again | Indicator toggles, order reverses |
| 3 | Multi-column | Click different column | Previous column indicator removed, new appears |

---

## Sort Assertion Strategy

**NEVER rely solely on `AssertChanged` of a DIFFERENT column's first row.**
If sort-by-Column-X leaves the same row at position 0 (data coincidence), the assertion falsely fails.

**Preferred approaches:**
1. Compare the sort column's FULL value sequence (before vs after)
2. Compare at least 3 rows' sort-column values
3. Verify sort indicator appeared/changed

**Acceptable:**
- `AssertChanged` on the SORT column's first value (not a different column)
- Check that the full row data array differs

**At minimum:**
- Capture sort column's first value
- Verify total row count unchanged
- Verify other state (buttons, messages) unchanged

---

## Sub-Grid Pagination

If a sub-grid has its own pagination:
- Test the same 6 scenarios on the sub-grid
- Sub-grids often have fewer records (10-50) — still verify pager format
- Sub-grid pagination is a common source of migration bugs

---

## Column Discovery (Step 1)

At page load, capture column headers:
```
runner.Capture("S0", "grid.columnMap", "0:Name|1:Status|2:Type|...");
```
This eliminates hardcoded column indices in later steps.

---

## Wait Times

- After pagination action: 3000ms (server data reload)
- After sort action: 3000ms (server re-query)
- After page size change: 3000ms (grid re-render)

Use SAME wait times in both reference and target tests for fair comparison.


---

## Module A Reference Design — 12-Step Template

Use this template when designing Module A (Page Load & Grid Inspection) for ANY page with a paginated, sortable grid. Customize selectors and column names — the STRUCTURE stays the same.

| Step | Action | Assertions | Key Evidence |
|------|--------|-----------|--------------|
| 1 | Page load | Heading, search controls, grid visible, pager | Row count, pager text "View 1-N of Z" |
| 2 | Capture column map | Grid headers → index mapping | "0:Name\|1:Status\|2:Type\|3:Date\|4:Size\|5:Actions" |
| 3 | Next page (1→2) | Pager advances, rows change | "View 11-20 of Z", firstRow ≠ step 1 |
| 4 | Previous page (2→1) | Returns to page 1 | "View 1-10 of Z", firstRow = step 1 |
| 5 | Last page | Jumps to final page | "View X-Z of Z" where X = Z - remainder + 1 |
| 6 | First page (from last→1) | Returns to start | "View 1-10 of Z", firstRow = step 1 |
| 7 | Page size change (10→25) | More rows visible, pager recalculates | Row count = 25, "View 1-25 of Z" |
| 8 | Restore page size (25→10) | Back to default | Row count = 10, "View 1-10 of Z" |
| 9 | Direct page input (page 3) | Jumps to page 3 | "View 21-30 of Z", page input = 3 |
| 10 | Sort column 1 ASC | Arrow ▲ appears, rows reorder | First row value changes from step 1 |
| 11 | Sort column 1 DESC | Arrow ▼ appears, rows reverse | First row different from step 10 |
| 12 | Sort column 2 | Column 1 arrow gone, column 2 arrow appears | Multi-column independence confirmed |

**Total assertions: ~60+ across 12 steps** (5+ assertions per step covering data, pager, indicators, and stability).

---

## Pager Info Math Formula

The "View X - Y of Z" text follows strict math. Validate at EVERY pagination step.

```
X = (currentPage - 1) × pageSize + 1
Y = min(currentPage × pageSize, Z)
Z = total records (CONSTANT — never changes during pagination)
```

### Validation Rules

| Check | Formula | Example (pageSize=10, Z=47) |
|-------|---------|----------------------------|
| First record on page | X = (page - 1) × size + 1 | Page 3: X = (3-1)×10+1 = 21 |
| Last record on page | Y = min(page × size, Z) | Page 3: Y = min(30, 47) = 30 |
| Last page record count | Y - X + 1 ≤ pageSize | Page 5: Y=47, X=41, count=7 ≤ 10 ✓ |
| Total never changes | Z at step N = Z at step 1 | Z = 47 at every step |

### Common Bugs Caught by Math Validation

| Bug | Symptom | Caught By |
|-----|---------|-----------|
| Off-by-one on first page | "View 0-9 of Z" | X formula: (1-1)×10+1 = 1, not 0 |
| Page size not applied | Row count stays 10 after changing to 25 | Y - X + 1 should = 25 |
| Total changes on filter | Z differs between steps without filter action | Z must be constant during pagination-only |
| Last page overcounts | "View 41-50 of 47" | Y = min(50, 47) = 47, not 50 |
| Direct input off-by-one | Page 3 shows "View 20-29" | X = (3-1)×10+1 = 21, not 20 |

### Per-Step Mandatory Captures

At EVERY pagination step, capture and log ALL of these:

```csharp
var pagerInfo = await GetPagerInfoText();      // "View X - Y of Z"
var currentPage = await GetCurrentPage();       // page number
var rowCount = await GetGridRowCount();         // visible rows
var firstRowValue = await GetFirstRowText();    // primary column value

// Log for audit trail
runner.Capture($"S{step}", "pager.info", pagerInfo);
runner.Capture($"S{step}", "pager.page", currentPage);
runner.Capture($"S{step}", "grid.rowCount", rowCount.ToString());
runner.Capture($"S{step}", "grid.row[0]", firstRowValue);
```

### Example: Full Math Validation for pageSize=10, Z=2942

| Step | Action | Expected X | Expected Y | Expected Z | Pager Text |
|------|--------|-----------|-----------|-----------|------------|
| 1 | Page load | 1 | 10 | 2942 | "View 1 - 10 of 2,942" |
| 3 | Next (page 2) | 11 | 20 | 2942 | "View 11 - 20 of 2,942" |
| 4 | Prev (page 1) | 1 | 10 | 2942 | "View 1 - 10 of 2,942" |
| 5 | Last (page 295) | 2941 | 2942 | 2942 | "View 2,941 - 2,942 of 2,942" |
| 6 | First (page 1) | 1 | 10 | 2942 | "View 1 - 10 of 2,942" |
| 7 | Size → 25 | 1 | 25 | 2942 | "View 1 - 25 of 2,942" |
| 8 | Size → 10 | 1 | 10 | 2942 | "View 1 - 10 of 2,942" |
| 9 | Direct page 3 | 21 | 30 | 2942 | "View 21 - 30 of 2,942" |

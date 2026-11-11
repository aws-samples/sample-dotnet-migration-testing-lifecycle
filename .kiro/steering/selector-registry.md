---
inclusion: manual
---

# Selector Registry — Proven Test Selectors

> **Purpose:** Before writing ANY selector in test code, check here first.
> Eliminates guessing and reduces selector-related test failures.
>
> **Updated during:** Testing lifecycle (Phase 1 and Phase 2).
> When a selector is confirmed working, add it here for reuse across modules.

---

## How to Add

After a selector is proven (test passes with it on both reference and target):
1. Add to the appropriate page section
2. Include: Element description, Reference selector, Target selector, Notes

---

## Global Selectors (Work on All Pages)

| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| | | | |

<!-- Add selectors that work across multiple pages: navigation, headers, etc. -->

---

## Page: PayorList

Reference = ASP.NET WebForms + free-jqGrid 4.15.5 (grid id `payorGrid`, pager id `payorPager`).
Target selectors to be filled during Phase 2 (Blazor + MudDataGrid). Proven on Module A reference.

Target = Blazor Server + MudBlazor `MudDataGrid` at `/Pages/PayorList` (port 5200). Target
selectors below are PROVEN on Module A (Phase 2). MudDataGrid has no per-row DOM id and no
jqGrid-style column ids — rows are read by cell index, headers by visible text.

### Grid
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| Data rows | `#payorGrid tr.jqgrow` | `tbody tr.mud-table-row` | Target has NO per-row id; read ID column via cell index 0 |
| Row Name cell text | `#payorGrid tr.jqgrow span.cell-name` | `tbody tr.mud-table-row td:nth-child(2)` | Column index 1 (0-based); display text inside `div.cell-display` |
| Row Code cell text | `#payorGrid tr.jqgrow span.cell-code` | `tbody tr.mud-table-row td:nth-child(3)` | Column index 2 (0-based) |
| Column header labels | `#gview_payorGrid thead tr.ui-jqgrid-labels th div[id^='jqgh_']` | `table.mud-table-root thead th .sortable-column-header` | Label = header text content |
| Hidden sizing row (ignore in diffs) | `#payorGrid tr.jqgfirstrow` | n/a | Reference-only layout noise |

### Pager
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| Pager info text | `#payorPager .ui-paging-info` | `.mud-table-pagination-caption` (LAST of two) | **Format differs**: target `1-5 of 15` vs reference `View 1 - 10 of 15`. First `.mud-table-pagination-caption` = "Rows per page:" label; last = range |
| Next / Prev | `#next_payorPager` / `#prev_payorPager` | `button[aria-label='Next Page']` / `button[aria-label='Previous Page']` | |
| First / Last | `#first_payorPager` / `#last_payorPager` | `button[aria-label='First Page']` / `button[aria-label='Last Page']` | |
| Direct page input | `#payorPager input.ui-pg-input` | n/a | **MudDataGridPager has no direct page-number box** — use Next/First to navigate |
| Page-size select | `#payorPager select.ui-pg-selbox` | `.mud-table-pagination .mud-select-input` (click) then `.mud-list-item` with option text | **Target default = 5** (reference = 10). Options 5/10/15/25 open in a popover list |

### Sort
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| Sort header (Name/Code) | `#jqgh_payorGrid_name` / `#jqgh_payorGrid_code` | `th .sortable-column-header` filtered by text "Payor Name" / "Code" | Click to sort/toggle |
| Sort indicator (asc/desc) | `#jqgh_payorGrid_{col} span.s-ico span.ui-icon-asc` / `...ui-icon-desc` | `th .sort-direction-icon.mud-direction-asc` / `.mud-direction-desc` | Active direction class on the sort icon button within the column's `th` |

### Filter / Buttons
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| Name filter | `#txtNameFilter` | `MudTextField` labelled "Payor Name" (`input` under the filter `MudPaper`) | _(to confirm in Module B)_ |
| Code filter | `#txtCodeFilter` | `MudTextField` labelled "Code" | _(to confirm in Module B)_ |
| Status filter | `#ddlActiveFilter` | `MudSelect` labelled "Status" (All/Active/Inactive) | _(to confirm in Module B)_ |
| Search / Clear | `#btnSearch` / `#btnClear` | `button.mud-button:has-text('Search')` / `:has-text('Clear')` | |
| Save All / Cancel | `#btnSaveAll` / `#btnCancelAll` | `button.mud-button:has-text('Save All')` / `:has-text('Cancel')` | Disabled unless dirty (proven: both disabled at load) |

---

## Page: {PageName} (copy this section per page)

### Navigation
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| | | | |

### Grid
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| | | | |

### Form Controls
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| | | | |

### Buttons / Actions
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| | | | |

### Dialogs
| Element | Reference Selector | Target Selector | Notes |
|---------|-------------------|-----------------|-------|
| | | | |

---
inclusion: manual
---

# CSS Guide — App-Specific Styling

> **Updated when:** CSS conflicts are discovered during testing (or migration).
> Document your app's CSS architecture, known conflicts, and resolutions here.

---

## CSS Architecture

### File Load Order

Document your app's CSS load order here. Order matters — later files override earlier ones.

```html
<!-- 1. Framework CSS (e.g., Bootstrap, MudBlazor, Tailwind) -->
<!-- 2. Legacy/third-party CSS -->
<!-- 3. App theme CSS -->
<!-- 4. App overrides (MUST load LAST) -->
```

---

## Known Conflicts

| Source CSS | Rule | Problem | Fix Applied |
|-----------|------|---------|-------------|
| | | | |

<!-- Add conflicts discovered during testing (or migration): which CSS file breaks what component -->

---

## Grid Styling

Document grid-specific CSS patterns here (column widths, sort icons, row alternation, hover).

---

## Form Control Styling

Document form control CSS here (input widths, dropdown styling, date picker).

---

## Dialog / Modal Styling

Document dialog CSS patterns here.

---

## Resolved Issues Log

| # | Date | Issue | Resolution | Page |
|---|------|-------|-----------|------|
| | | | | |

<!-- Track CSS fixes chronologically so future developers understand what was done and why -->

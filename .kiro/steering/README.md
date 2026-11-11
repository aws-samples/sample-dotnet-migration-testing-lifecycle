---
inclusion: manual
---

# Steering Documents — Index & Routing Guide

These documents define the **methodology, process, standards, and app-specific knowledge**
for testing web applications. All steering docs live in `.kiro/steering/`. They are of two
kinds, distinguished by filename:

## 1. Reusable methodology docs (app-agnostic — the "knowledge base")

These are the same for every application. Treat them as the immutable core; improve them
only when the *process/methodology itself* gets better.

| Document | Purpose | Inclusion |
|----------|---------|-----------|
| `testing-lifecycle.md` | Testing process (Phases 0–6, state-machine model; mapping to the runner and chat phases: [Phase numbering](../workflows/README.md#phase-numbering)) | manual |
| `state-machine-testing.md` | Module design, state/transition definitions | manual |
| `module-test-standards.md` | Test code patterns, assertions, CSS capture | manual |
| `css-comparison-methodology.md` | CSS capture, diff methodology, severity | manual |
| `preflight.md` | Service startup, auth, health check | manual |
| `phase-0-analysis.md` … `phase-5-6-completion.md` | Per-phase detailed steps | manual |
| `pagination-sorting-standards.md` | Standard grid pagination + sort patterns | manual |
| `reporting-standards.md` | Mandatory report formats, PASS/FAIL rules | **always** |
| `lifecycle-chat-display.md` | Relay `CHAT-RENDER <path>` notifications from the test-page workflow into the chat verbatim | **always** |

## 2. App-specific docs (this application's discovered knowledge)

These start near-empty and grow as you test a specific app. For a **new project**,
reset these to empty (see the repo root README "Starting a new project").

| Document | Purpose | When to Update |
|----------|---------|---------------|
| `selector-registry.md` | Proven test selectors per page | During testing Phase 1+ |
| `css-guide.md` | App CSS architecture, conflicts, resolutions | When CSS issues found |

`css-guide.md` also exists in the migration-lifecycle repo; keep the two copies in sync by hand.
Migration registries live in that repo.

---

## Discovery Routing Rules — Where a New Learning Goes

When you discover something new during testing, record it in the right doc:

| Discovery Type | Where It Goes | Example |
|---|---|---|
| Better process step, gate logic, phase improvement | the relevant phase/lifecycle doc | "Add a pre-check before Phase 2 starts" |
| Test methodology improvement (assertion strategy) | `module-test-standards.md` | "Sort tests should compare full column sequence" |
| CSS comparison rule (ignore pattern, severity) | `css-comparison-methodology.md` | "±1px on dimensions is noise, auto-accept" |
| App-specific proven selector | `selector-registry.md` | "Sort header: `span.sortable-column-header`" |
| App-specific CSS conflict | `css-guide.md` | "gumby.css breaks MudSelect fieldset" |
| Bug in test framework code | `.kiro/test-project/` (fix the source) | "ModuleTestRunner HTML diff has false positives" |

## Precedence

When a methodology doc and an app-specific doc conflict on the same topic,
**the app-specific doc wins** — it represents tested, proven patterns for THIS application.

## Auto-loaded vs manual

All docs are `inclusion: manual` (referenced on demand via `#doc-name.md`) **except**
`reporting-standards.md`, which is `inclusion: always` so its PASS/FAIL and report-format
rules apply across every phase automatically, and `lifecycle-chat-display.md`, which is
`inclusion: always` so the main chat pastes every workflow chat render (plan, tests
authored, module results, run summary, fix rounds, final report) as tables.

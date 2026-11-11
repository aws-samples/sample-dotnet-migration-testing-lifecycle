---
inclusion: manual
---

# Pre-Flight — Owned by the Runner

Reference: `#preflight.md`

Agents do not do preflight. `.\.kiro\test-project\lifecycle.ps1` runs it automatically as
step 0 of `run-page`, `rerun-target` and `preflight`. Never start apps by hand, never poll
ports, never use `Test-NetConnection` or sleep loops.

---

## 1. Doctor (environment checklist)

```powershell
.\.kiro\test-project\lifecycle.ps1 -Action doctor
```

Prints one `[PASS]` / `[FAIL]` / `[WARN]` line per check; every FAIL includes the exact fix.
Writes `.kiro/test-project/doctor-result.json` and, with `-Page`, the `## Environment`
section of that page's PROGRESS.md. Any required FAIL = exit 3, nothing runs.

| Check | Required |
|-------|----------|
| dotnet SDK (major versions read from the csprojs, currently 10) | yes |
| Playwright browsers for the csproj's Microsoft.Playwright version (auto-installed after build if missing) | yes |
| Settings (`appsettings.test.json`: URLs, `Apps[].project`, `healthAccept`) | yes |
| Ports free or owned by the expected app | yes |
| Repo is the workspace (open the repo folder itself in Kiro) | yes |
| Zero-prompt rules installed (from `.kiro/settings/permissions.yaml`) | yes |
| git available | advisory |
| Kiro workflows enabled | advisory |

---

## 2. App auto-start

For each entry of `Apps` in `.kiro/test-project/appsettings.test.json` (in order: service,
reference, target) the runner checks `healthUrl`. If the app is down it starts
`dotnet run --project <project>` hidden, logs to
`.kiro/app-testing/<Page>/logs/<name>.log` and `<name>.err.log`, and waits up to 120 s.
If it never comes up: exit 3 and the last 20 log lines.

**`healthAccept`:**
- `status200` (default): up when the URL returns HTTP 200.
- `anyResponse`: up on any HTTP response, including 500, and the process owning the port
  matches the csproj. Use it for SOAP/WCF `.svc` endpoints that reject a plain GET.

The app marked `isTarget: true` is rebuilt and restarted when its sources are newer than
the running process (stale Target).

---

## 3. Authentication (only when configured)

Runs only when `Auth.LoginUrl` is set. The bundled sample has no auth.

---

## 4. Failure handling

| Exit | Meaning | What to do |
|------|---------|------------|
| 3 | Environment (doctor FAIL, app did not start, browsers missing) | Rerun once; then report the `[FAIL]` line and its fix |
| 1 | Harness (build failure, missing test, lock held) | See the step's exit-code table |

# Security

## Non-Production Disclaimer

This repository is a reference implementation for educational and development purposes. It is
not intended for direct production use. Before using any part of it in a production setting,
add your own hardening, testing and security review.

## Reporting Vulnerabilities

If you discover a potential security issue in this project, notify AWS Security via
[aws-security@amazon.com](mailto:aws-security@amazon.com) or the
[vulnerability reporting page](http://aws.amazon.com/security/vulnerability-reporting/).
Please do not create a public GitHub issue.

## AWS Services Used

None of the code calls AWS APIs. The tooling runs locally on a Windows machine with the .NET
SDK and a Playwright-driven Chromium browser, against web apps reachable by URL (by default
the bundled sample on localhost).

The workflow runs in **Kiro**. Its agents send repository files, command output and captured
page content to Kiro's AI service as part of normal chat use. Don't run it against pages with
confidential data.

## Known Security Considerations

These are accepted trade-offs for a local demo and test tool:

1. **Zero-prompt permissions template.** `.kiro/settings/permissions.yaml` allows every shell
   command, every write inside the workspace and every file read on the machine (not only
   inside the workspace) without an approval prompt. It denies only force-push,
   `git reset --hard`, `git clean`, `--no-verify`, reads and writes of secret files (`.env`,
   keys, `.ssh`, AWS credentials) and writes to the Reference source; `git push` and
   `git commit` still ask. Anything else, for example a recursive delete, a download or a read
   of a file in another folder, runs without asking.
   Run the harness only against pages you own and trust: page content is processed by an AI
   agent with shell access.
2. **AI agents edit source code.** The workflow's agents write test code and change the
   Target app's source (auto-fix rounds). Review all agent output before you commit or use it.
3. **Local apps with no authentication.** The runner starts the sample apps on fixed local
   ports 5000 (service), 5100 (Reference) and 5200 (Target) over plain HTTP, with no
   authentication.
4. **Sample app.** The bundled sample has no authentication, no input hardening and an
   in-memory data store. Its Reference (`sample/PayorContracts.Legacy`) is a static `.aspx`
   simulation that loads jQuery, jQuery UI, jquery-validation, free-jqgrid, DataTables and
   flatpickr from public CDNs at runtime; each tag is version-pinned and carries a `sha384`
   Subresource Integrity hash with `crossorigin="anonymous"`.
5. **Captured page content.** Screenshots, reports (markdown, PDF, DOCX) and run history
   record whatever the tested pages show. Do not run the tool against pages that show real
   customer or personal data without reviewing where that output goes.
6. **Local run data.** Results, reports, logs and history are written under
   `.kiro/app-testing/` (git-ignored). If you configure `Auth`, the browser session is saved
   to `auth-state.json` (git-ignored); treat it as a credential.
7. **NuGet packages.** All NuGet packages are pinned to releases with no known advisories as
   of 2026-11-09 (`dotnet list package --vulnerable --include-transitive` reports none).
   Re-run before reusing sample code.

## Production Hardening Recommendations

- Review the permissions template and narrow it before you install it. The shipped template
  stays zero-prompt by design; for a production-shaped setup, replace `shell: allow "*"` with
  an allowlist (for example `dotnet *`, `pwsh ./.kiro/test-project/lifecycle.ps1 *`,
  `git status*`, `git diff*`), narrow `fs_read` to `./**`, and run the harness only against
  pages you own and trust.
- Keep the sample apps on localhost. Add HTTPS and an authentication scheme before any
  non-local use (see [sample/README.md](sample/README.md)).
- Run the tool only against non-production environments that hold test data.
- If the app needs a login, use a least-privilege test account for `Auth`.
- Review every agent change (tests and Target fixes) before you commit it.
- Keep dependencies updated: run `dotnet list package --vulnerable --include-transitive` and
  upgrade any package it reports before reusing sample code.
- The Reference pages load their browser libraries from public CDNs with pinned versions and
  `sha384` Subresource Integrity hashes. If you change a version, recompute its hash.

## Resource Cleanup

- Stop the sample apps (`Get-Process -Name "PayorContracts*" | Stop-Process`) or close their
  terminals.
- Delete the local run data: `.kiro/app-testing/` and `auth-state.json` if present.
- Remove the installed permission rules from Kiro ("Kiro: Open Permissions (Workspace)",
  delete the rules, save).
- Delete the generated `.kiro/test-project/doctor-result.json` and `.kiro/test-project/logs/`
  if present.
- Optionally remove the Playwright browsers (`%LOCALAPPDATA%\ms-playwright`) and revert the
  ExecutionPolicy with `Set-ExecutionPolicy -Scope CurrentUser Undefined`.

## Dependencies

| Package | Version | Used by | Purpose |
|---|---|---|---|
| NUnit | 4.3.2 | Harness, Harness.Tests, module tests | Test framework |
| NUnit3TestAdapter | 4.6.0 | Harness.Tests, module tests | Test runner adapter |
| Microsoft.NET.Test.Sdk | 17.13.0 | Harness.Tests, module tests | Test host |
| Microsoft.Playwright | 1.52.0 | Harness, Harness.Tests, module tests | Browser automation |
| Microsoft.Playwright.NUnit | 1.52.0 | Harness.Tests, module tests | Playwright NUnit integration |
| Microsoft.Extensions.Configuration.Json | 10.0.0 | Harness | Settings loading |
| Microsoft.Extensions.Configuration.Binder | 10.0.0 | Harness | Settings binding |
| DocumentFormat.OpenXml | 3.2.0 | Harness | DOCX reports |
| PDFsharp-MigraDoc | 6.2.4 | Harness | PDF reports |
| System.Security.Cryptography.Pkcs | 10.0.14 | Harness | Pin over a vulnerable transitive version |
| CoreWCF.Http | 1.9.1 | sample Host | WCF service hosting |
| CoreWCF.Primitives | 1.9.1 | sample Host | WCF service hosting |
| Microsoft.AspNetCore.Authentication.Negotiate | 10.0.14 | sample Host | Authentication scheme required by CoreWCF |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.14 | sample Client | Configuration |
| Microsoft.Extensions.Configuration.Binder | 10.0.14 | sample Client | Configuration binding |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | sample Client | Dependency injection |
| Microsoft.Extensions.Hosting | 10.0.14 | sample Scheduler | Worker host |
| Microsoft.Extensions.Options | 10.0.14 | sample Client | Options pattern |
| Microsoft.Extensions.Options.DataAnnotations | 10.0.14 | sample Client | Options validation |
| System.Security.Cryptography.Pkcs | 10.0.14 | sample Client | Pin over a vulnerable transitive version |
| System.Security.Cryptography.Xml | 10.0.14 | sample Client | Pin over a vulnerable transitive version |
| MudBlazor | 8.6.0 | sample Web | Blazor UI components |
| System.ServiceModel.Http | 8.1.0 | sample Web, Client, Legacy | WCF client |
| System.ServiceModel.Primitives | 8.1.0 | sample Web, Client, Legacy | WCF client |

Playwright downloads a Chromium build at first run; it is not part of this repository. Audit
these packages regularly and keep versions pinned exactly in the csproj files. Licenses are
listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

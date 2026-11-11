# Third-party notices

## Test tooling (`.kiro/test-project/`)

NuGet packages referenced directly by the test tooling under `.kiro/test-project/`
(Harness, Harness.Tests, Reports and the generated module projects). Versions are pinned
exactly in the csproj files; keep this table in sync when a version changes.

| Package | Version | License | URL |
|---|---|---|---|
| NUnit | 4.3.2 | MIT | https://github.com/nunit/nunit |
| NUnit3TestAdapter | 4.6.0 | MIT | https://github.com/nunit/nunit3-vs-adapter |
| Microsoft.NET.Test.Sdk | 17.13.0 | MIT | https://github.com/microsoft/vstest |
| Microsoft.Playwright | 1.52.0 | MIT | https://github.com/microsoft/playwright-dotnet |
| Microsoft.Playwright.NUnit | 1.52.0 | MIT | https://github.com/microsoft/playwright-dotnet |
| Microsoft.Extensions.Configuration.Json | 10.0.0 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Configuration.Binder | 10.0.0 | MIT | https://github.com/dotnet/runtime |
| DocumentFormat.OpenXml | 3.2.0 | MIT | https://github.com/dotnet/Open-XML-SDK |
| PDFsharp-MigraDoc | 6.2.4 | MIT | https://www.nuget.org/packages/PDFsharp-MigraDoc/6.2.4 |
| PDFsharp (dependency of PDFsharp-MigraDoc) | 6.2.4 | MIT | https://www.nuget.org/packages/PDFsharp/6.2.4 |
| System.Security.Cryptography.Pkcs (direct pin over the 8.0.1 that PDFsharp pulls in, which has advisories) | 10.0.14 | MIT | https://github.com/dotnet/runtime |

## Sample applications (`sample/`)

NuGet packages referenced directly by the sample applications under `sample/`.

| Package | Version | License | Used by | URL |
|---|---|---|---|---|
| CoreWCF.Http | 1.9.1 | MIT | PayorContracts.Host | https://github.com/CoreWCF/CoreWCF |
| CoreWCF.Primitives | 1.9.1 | MIT | PayorContracts.Host | https://github.com/CoreWCF/CoreWCF |
| Microsoft.AspNetCore.Authentication.Negotiate | 10.0.14 | MIT | PayorContracts.Host | https://github.com/dotnet/aspnetcore |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.14 | MIT | PayorContracts.Client | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Configuration.Binder | 10.0.14 | MIT | PayorContracts.Client | https://github.com/dotnet/runtime |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | MIT | PayorContracts.Client | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | PayorContracts.Scheduler | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Options | 10.0.14 | MIT | PayorContracts.Client | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Options.DataAnnotations | 10.0.14 | MIT | PayorContracts.Client | https://github.com/dotnet/runtime |
| System.Security.Cryptography.Pkcs (direct pin over the 8.0.x pulled in transitively) | 10.0.14 | MIT | PayorContracts.Client | https://github.com/dotnet/runtime |
| System.Security.Cryptography.Xml (direct pin over the 8.0.x pulled in transitively) | 10.0.14 | MIT | PayorContracts.Client | https://github.com/dotnet/runtime |
| MudBlazor | 8.6.0 | MIT | PayorContracts.Web | https://github.com/MudBlazor/MudBlazor |
| System.ServiceModel.Http | 8.1.0 | MIT | PayorContracts.Web, PayorContracts.Client, PayorContracts.Legacy | https://github.com/dotnet/wcf |
| System.ServiceModel.Primitives | 8.1.0 | MIT | PayorContracts.Web, PayorContracts.Client, PayorContracts.Legacy | https://github.com/dotnet/wcf |

Licenses in both tables are the `<license type="expression">` values from each package's
nuspec in the local NuGet cache.

Notes:

- Stay on PDFsharp-MigraDoc 6.2.x. Later releases changed license (for example 6.5.0 is
  published as AGPL-3.0-only), so check the nuspec license of any newer version before upgrading.
- Playwright downloads browser binaries (Chromium) at runtime; they are not part of this
  repository and come under their own licenses.
- Transitive dependencies are not listed; they come under their own licenses (see each
  package's nuspec).
- The `PayorContracts.Legacy` `.aspx` pages load the browser libraries below from public CDNs
  at runtime. They are not stored in this repository. Each tag is version-pinned and carries
  a `sha384` Subresource Integrity hash plus `crossorigin="anonymous"`; if you change a
  version, recompute the hash from the file served at the new URL.

| Library | Version | License | URL |
|---|---|---|---|
| jQuery | 3.7.1 | MIT | https://jquery.com/ |
| jQuery UI | 1.13.2 | MIT | https://jqueryui.com/ |
| jQuery Validation | 1.19.5 | MIT | https://github.com/jquery-validation/jquery-validation |
| free-jqGrid | 4.15.5 | MIT OR GPL-2.0 | https://github.com/free-jqgrid/jqGrid |
| flatpickr | 4.6.13 | MIT | https://github.com/flatpickr/flatpickr |
| DataTables | 1.13.7 | MIT | https://datatables.net/ |

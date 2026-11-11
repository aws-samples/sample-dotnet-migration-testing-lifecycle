# PayorContracts sample

A small, fictional payor and contracts admin app used to try the migration and testing
lifecycles. It has an old-style page set (Reference) and a Blazor version (Target) that read
the same data from one service. All data is fictional; see
[../docs/SAMPLE-DATA.md](../docs/SAMPLE-DATA.md).

## Projects

| Project | Role | URL |
|---|---|---|
| `PayorContracts.Host` | CoreWCF service with an in-memory data store | http://localhost:5000/PayorInformation.svc |
| `PayorContracts.Legacy` | Reference: WebForms-style `.aspx` pages (jQuery, jqGrid, DataTables, loaded from public CDNs) served as static files by ASP.NET Core | http://localhost:5100/Pages/PayorList.aspx |
| `PayorContracts.Web` | Target: Blazor Server with MudBlazor | http://localhost:5200/Pages/PayorList |
| `PayorContracts.Client` | Shared WCF client library (ChannelFactory, options, DI extensions) | - |
| `PayorContracts.WebApi` | Web API that calls the service through the client library | - |
| `PayorContracts.Scheduler` | Worker service that calls the service through the client library | - |

Solution: `PayorContracts.sln`. All projects target .NET 10.

The Reference is a simulation: `PayorContracts.Legacy` is ASP.NET Core serving the `.aspx`
pages as static HTML, not real WebForms.

## How to run

`BLAZOR-MIGRATION-GUIDE.md` is copied from the migration-lifecycle repo; it is reference
only and not used by the tests here.

See [RUNNING.md](RUNNING.md). In short, start the service first, then the Reference and the
Target:

```powershell
dotnet run --project PayorContracts.Host/PayorContracts.Host.csproj
dotnet run --project PayorContracts.Legacy/PayorContracts.Legacy.csproj
dotnet run --project PayorContracts.Web/PayorContracts.Web.csproj
```

The testing-lifecycle runner starts all three apps when they are not running. The
migration-lifecycle runner starts only the service (5000) and the Target (5200) for its smoke
check; start the Reference (5100) yourself when you need it.

## Configuration notes

- The sample has no authentication. `CoreWcf:Binding:Security:Mode` is `None` in
  `PayorContracts.Host/appsettings*.json`. `PayorContracts.Host/Program.cs` registers the
  Negotiate authentication handler only because the CoreWCF pipeline needs an
  authentication scheme provider; with security mode `None` no credentials are requested.
  The Windows security options in the code show the configuration pattern only.
- `ConnectionStrings:PayorContractsDatabase` and `AppSettings:SSRSReportServerUrl` in
  `PayorContracts.Host/appsettings*.json` are local placeholders. The service reads them at
  startup and fails if they are missing, but never connects to them: all data is a static
  in-memory list. Keep the keys; the values do not need to point at anything.
- The apps bind to `http://localhost` on ports 5000, 5100 and 5200 (plain HTTP, no
  authentication). Keep them on localhost: do not rebind them to all interfaces or to any
  other network interface. Add HTTPS and an authentication scheme before any non-local use.
- The sample packages are pinned to releases with no known advisories. Re-check with
  `dotnet list PayorContracts.sln package --vulnerable --include-transitive` before reusing
  the code. See [../SECURITY.md](../SECURITY.md).

# Running the PayorContracts Sample Applications

This guide covers how to run the CoreWCF service, the legacy WebForms app, and the Blazor Server app.

## Prerequisites

- .NET 10 SDK installed
- Two or three terminal windows

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        CoreWCF Service                           │
│                   http://localhost:5000                          │
│              /PayorInformation.svc (BasicHttpBinding)            │
└──────────────────────────┬──────────────────────────────────────┘
                           │ WCF ChannelFactory
              ┌────────────┴────────────┐
              │                         │
┌─────────────┴───────────┐  ┌──────────┴──────────────┐
│   Legacy Web App        │  │   Blazor Server App      │
│   http://localhost:5100  │  │   http://localhost:5200  │
│   (WebForms + ASMX)     │  │   (MudBlazor DataGrid)   │
└─────────────────────────┘  └──────────────────────────┘
```

---

## Step 1: Start the CoreWCF Service (Required for both apps)

```bash
dotnet run --project PayorContracts.Host/PayorContracts.Host.csproj
```

Verify it's running:
- Open `http://localhost:5000/PayorInformation.svc` in a browser. It should answer (any HTTP response means it is up; SOAP/WCF services usually reject a plain GET).

---

## Step 2a: Run the Legacy WebForms App

In a second terminal:

```bash
dotnet run --project PayorContracts.Legacy/PayorContracts.Legacy.csproj
```

Open in browser:
- **Page:** `http://localhost:5100/Pages/PayorList.aspx`
- **ASMX endpoint (POST only, called by the page):** `http://localhost:5100/WebServices/PayorService.asmx/GetPayors`

Features:
- jqGrid grid (free-jqGrid) loaded from the ASMX endpoint, with server-side pagination
- Filter by name, code, and status
- Column sorting (click headers)
- Page size selector (5, 10, 15, 25)

---

## Step 2b: Run the Blazor Server App

In a second (or third) terminal:

```bash
dotnet run --project PayorContracts.Web/PayorContracts.Web.csproj
```

Open in browser:
- **Pages:** `http://localhost:5200/Pages/PayorList`, `http://localhost:5200/Pages/AddPayor`, `http://localhost:5200/Pages/PayorEdit`, `http://localhost:5200/Pages/ContractHierarchy`

In this repo PayorList is already migrated. `http://localhost:5200` and
`http://localhost:5200/Pages/PayorList` serve the Target page that the tests compare.

Features:
- MudBlazor components (DataGrid, chips, outlined inputs, responsive grid)
- No JavaScript — pure Blazor Server with SignalR

---

## Running Both Legacy and Blazor Side-by-Side

You can run all three simultaneously to compare:

```bash
# Terminal 1 — CoreWCF service
dotnet run --project PayorContracts.Host/PayorContracts.Host.csproj

# Terminal 2 — Legacy WebForms
dotnet run --project PayorContracts.Legacy/PayorContracts.Legacy.csproj

# Terminal 3 — Blazor Server
dotnet run --project PayorContracts.Web/PayorContracts.Web.csproj
```

| App | URL | Port |
|---|---|---|
| CoreWCF Service | http://localhost:5000/PayorInformation.svc | 5000 |
| Legacy WebForms | http://localhost:5100/Pages/PayorList.aspx | 5100 |
| Blazor Server | http://localhost:5200/Pages/PayorList | 5200 |

---

## Troubleshooting

| Issue | Fix |
|---|---|
| Port already in use | Kill the process: `netstat -ano \| findstr :5000` then `taskkill /PID <pid> /F` |
| CoreWCF returns 500 | Check that `appsettings.Development.json` has `Security.Mode: "None"` |
| Legacy grid shows "No records" | Ensure CoreWCF is running on port 5000 first |
| Blazor page blank | Check browser console for SignalR connection errors |
| Build fails (file locked) | Stop running processes: `Get-Process -Name "PayorContracts*" \| Stop-Process -Force` (covers Host, Legacy and Web) |

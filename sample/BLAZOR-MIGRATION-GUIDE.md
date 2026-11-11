# WebForms to Blazor Server Migration Guide

## Migration Prompt / Instructions

Use this as a reference prompt when migrating ASP.NET WebForms pages to Blazor Server components.

---

## Core Migration Rules

1. **No jQuery** — replace all `$()` calls with Blazor `@bind`, `@onclick`, `@if` directives
2. **No JavaScript** — replace `$.ajax()` with DI-injected C# service calls
3. **No ASMX web services** — replace with C# service classes registered in DI
4. **No `<script>` tags** — all logic lives in `@code { }` blocks
5. **No DOM manipulation** — Blazor re-renders declaratively based on state changes
6. **No `runat="server"`** — Blazor components don't use WebForms server controls
7. **No ViewState** — use component fields/properties for state
8. **No Page lifecycle** (`Page_Load`, `Page_Init`) — use `OnInitialized()`, `OnParametersSet()`
9. **No UpdatePanel** — Blazor handles partial updates automatically via SignalR
10. **No ScriptManager** — not needed, Blazor manages its own connection

---

## Component-by-Component Migration Map

### jQuery DataTables → MudBlazor DataGrid

**Before (jQuery):**
```javascript
$('#payorGrid').DataTable({
    processing: true,
    serverSide: true,
    pageLength: 5,
    columns: [
        { data: 'id', title: 'ID' },
        { data: 'name', title: 'Payor Name' }
    ],
    ajax: function(data, callback) {
        $.ajax({
            url: '/WebServices/PayorService.asmx/GetPayors',
            type: 'POST',
            data: JSON.stringify(request),
            success: function(response) { callback(response); }
        });
    }
});
```

**After (Blazor):**
```razor
<MudDataGrid Items="@payors" SortMode="SortMode.Single" Dense="true" Striped="true">
    <Columns>
        <PropertyColumn Property="x => x.Id" Title="ID" />
        <PropertyColumn Property="x => x.Name" Title="Payor Name" />
    </Columns>
    <PagerContent>
        <MudDataGridPager T="PayorDto" PageSizeOptions="new int[] { 5, 10, 15, 25 }" />
    </PagerContent>
</MudDataGrid>

@code {
    private List<PayorDto> payors = new();

    protected override void OnInitialized()
    {
        payors = PayorService.GetAllPayors();  // Direct C# call, no AJAX
    }
}
```

---

### jQuery AJAX → DI Service Injection

**Before (jQuery AJAX):**
```javascript
$.ajax({
    url: '/WebServices/PayorService.asmx/GetPayors',
    type: 'POST',
    contentType: 'application/json',
    data: JSON.stringify({ nameFilter: 'Acme', page: 1, pageSize: 10 }),
    success: function(response) {
        // update DOM
    }
});
```

**After (Blazor):**
```razor
@inject PayorService PayorService

@code {
    private void LoadData()
    {
        // Direct method call — no HTTP, no JSON, no callbacks
        var result = PayorService.GetAllPayors();
    }
}
```

---

### HTML Input + jQuery `.val()` → Blazor `@bind`

**Before:**
```html
<input type="text" id="txtNameFilter" />
<script>
    var name = $('#txtNameFilter').val();
</script>
```

**After:**
```razor
<MudTextField @bind-Value="nameFilter" Label="Payor Name" />

@code {
    private string nameFilter = string.Empty;
    // nameFilter is always in sync — no manual DOM reading
}
```

---

### jQuery `$('#btn').on('click', fn)` → Blazor `@onclick`

**Before:**
```javascript
$('#btnSearch').on('click', function() {
    dataTable.ajax.reload();
});
```

**After:**
```razor
<MudButton OnClick="ApplyFilters">Search</MudButton>

@code {
    private void ApplyFilters()
    {
        // filter logic here — UI re-renders automatically
    }
}
```

---

### `<select>` + jQuery Change Handler → Blazor `@bind`

**Before:**
```html
<select id="ddlStatus">
    <option value="">All</option>
    <option value="true">Active</option>
</select>
<script>
    $('#ddlStatus').on('change', function() { reload(); });
</script>
```

**After:**
```razor
<MudSelect @bind-Value="statusFilter" Label="Status">
    <MudSelectItem Value="@("")">All</MudSelectItem>
    <MudSelectItem Value="@("true")">Active</MudSelectItem>
    <MudSelectItem Value="@("false")">Inactive</MudSelectItem>
</MudSelect>

@code {
    private string statusFilter = string.Empty;
}
```

---

### Conditional Rendering (jQuery `.show()/.hide()`) → Blazor `@if`

**Before:**
```javascript
$('#loadingIndicator').show();
// ... after load
$('#loadingIndicator').hide();
```

**After:**
```razor
@if (isLoading)
{
    <MudProgressLinear Color="Color.Primary" Indeterminate="true" />
}
```

---

### Status Display (jQuery `.html()`) → Blazor `<TemplateColumn>`

**Before:**
```javascript
render: function(data) {
    return data ? '<span class="status-active">Active</span>'
                : '<span class="status-inactive">Inactive</span>';
}
```

**After:**
```razor
<TemplateColumn Title="Status">
    <CellTemplate>
        @if (context.Item.IsActive)
        {
            <MudChip T="string" Color="Color.Success" Size="Size.Small">Active</MudChip>
        }
        else
        {
            <MudChip T="string" Color="Color.Error" Size="Size.Small">Inactive</MudChip>
        }
    </CellTemplate>
</TemplateColumn>
```

---

### ASMX Web Service → DI Service Class

**Before (ASMX):**
```csharp
[WebService]
[ScriptService]
public class PayorService : WebService
{
    [WebMethod]
    [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
    public PayorGridResponse GetPayors(string nameFilter, int page, int pageSize)
    {
        var data = WcfClientHelper.CallService(svc => svc.GetAllPayors());
        // filter, sort, page...
        return response;
    }
}
```

**After (DI Service):**
```csharp
public class PayorService
{
    private readonly string _endpointUrl;

    public PayorService(IConfiguration configuration)
    {
        _endpointUrl = configuration["WcfService:EndpointUrl"]!;
    }

    public List<PayorDto> GetAllPayors()
    {
        var binding = new BasicHttpBinding(BasicHttpSecurityMode.None);
        var factory = new ChannelFactory<IPayorInformation>(binding, new EndpointAddress(_endpointUrl));
        var channel = factory.CreateChannel();
        return channel.GetAllPayors().Payors;
    }
}
```

Registered in `Program.cs`:
```csharp
builder.Services.AddScoped<PayorService>();
```

Injected in component:
```razor
@inject PayorService PayorService
```

---

### Page Lifecycle → Component Lifecycle

| WebForms | Blazor | When |
|---|---|---|
| `Page_Init` | `OnInitialized()` | Component first created |
| `Page_Load` | `OnInitialized()` or `OnParametersSet()` | After init or when parameters change |
| `Page_PreRender` | `OnAfterRender(bool firstRender)` | After DOM rendered |
| `IsPostBack` check | Not needed | Blazor doesn't use postbacks |
| `ViewState["key"]` | Private field in `@code` | Component state lives in memory |

---

### Web.config WCF Client → appsettings.json + IConfiguration

**Before (Web.config):**
```xml
<client>
    <endpoint name="PayorInformationEndpoint"
              address="http://localhost:5000/PayorInformation.svc"
              binding="basicHttpBinding" />
</client>
```

**After (appsettings.json):**
```json
{
    "WcfService": {
        "EndpointUrl": "http://localhost:5000/PayorInformation.svc"
    }
}
```

Read in service constructor:
```csharp
public PayorService(IConfiguration configuration)
{
    _endpointUrl = configuration["WcfService:EndpointUrl"]!;
}
```

---

## MudBlazor Setup Checklist

1. Add NuGet: `MudBlazor` (latest version)
2. In `Program.cs`: `builder.Services.AddMudServices();`
3. In `_Imports.razor`: `@using MudBlazor`
4. In `App.razor` `<head>`:
   - `<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />`
5. In `App.razor` `<body>` (before closing):
   - `<script src="_content/MudBlazor/MudBlazor.min.js"></script>`
6. In `MainLayout.razor`:
   - `<MudThemeProvider />`, `<MudPopoverProvider />`, `<MudDialogProvider />`, `<MudSnackbarProvider />`

---

## Key Differences to Remember

| Concept | WebForms/jQuery | Blazor Server |
|---|---|---|
| UI updates | Manual DOM manipulation | Automatic re-render on state change |
| Data flow | AJAX → JSON → parse → update DOM | C# method call → assign field → UI updates |
| Event handling | jQuery event listeners | `@onclick`, `@onchange`, `@bind` |
| State management | ViewState, Session, hidden fields | Component fields, cascading parameters, DI services |
| Partial updates | UpdatePanel, AJAX | Built-in via SignalR (every render is partial) |
| Validation | jQuery Validate plugin | `<EditForm>` + `<DataAnnotationsValidator>` |
| Navigation | `Response.Redirect`, hyperlinks | `NavigationManager.NavigateTo()`, `<NavLink>` |
| Authentication | Forms Auth, Windows Auth | ASP.NET Core Identity, `[Authorize]`, `<AuthorizeView>` |

using PayorContracts.Web.Components;
using PayorContracts.Web.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor services
builder.Services.AddMudServices(config =>
{
    // Reference shows no close control on its status messages
    config.SnackbarConfiguration.ShowCloseIcon = false;
});

// Register the payor service (replaces ASMX web service layer)
builder.Services.AddScoped<PayorAccessGuard>();
builder.Services.AddScoped<PayorService>();
builder.Services.AddScoped<ContractHierarchyService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

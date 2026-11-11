using Microsoft.AspNetCore.Authentication.Negotiate;
using PayorContracts.Host.Extensions;
using PayorContracts.Host.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppConfiguration();       // loads appsettings + binds IOptions
builder.AddCoreWcfServices();        // registers CoreWCF DI services

// Register service implementations in DI (required because they have constructor dependencies)
builder.Services.AddTransient<PayorInformation>();
builder.Services.AddTransient<ContractHierarchy>();

// Required by CoreWCF — registers authentication services for Windows/Negotiate auth.
// Even with SecurityMode.None, CoreWCF's pipeline requires IAuthenticationSchemeProvider.
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseCoreWcfEndpoints();           // configures all WCF endpoints, behaviors, and logging from IOptions

app.Run();

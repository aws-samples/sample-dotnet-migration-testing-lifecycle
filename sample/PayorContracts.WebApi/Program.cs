using PayorContracts.Client.Extensions;
using PayorContracts.Host.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        optional: true,
        reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddCoreWcfClientConfiguration(builder.Configuration);
builder.Services.AddCoreWcfClient<IPayorInformation>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.Run();

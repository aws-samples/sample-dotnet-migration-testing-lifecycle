using PayorContracts.Client.Extensions;
using PayorContracts.Host.Contracts;
using PayorContracts.Scheduler;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        optional: true,
        reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddCoreWcfClientConfiguration(builder.Configuration);
builder.Services.AddCoreWcfClient<IPayorInformation>();
builder.Services.AddHostedService<TerminateContractWorker>();

var host = builder.Build();
host.Run();

using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using PayorContracts.Legacy.WebServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<PayorServiceClient>();
builder.Services.AddSingleton<ContractServiceClient>();

var app = builder.Build();

// Serve .aspx files as HTML (ASP.NET Core doesn't recognize .aspx by default)
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".aspx"] = "text/html";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider
});

app.MapControllers();

// Default page when browsing root
app.MapGet("/", () => Results.Redirect("/Pages/PayorList.aspx"));

app.Run();

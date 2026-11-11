using PayorContracts.Host.Configuration;

namespace PayorContracts.Host.Extensions;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Loads appsettings.json, appsettings.{Environment}.json, and environment variables.
    /// Binds CoreWcf section to IOptions<CoreWcfOptions> with startup validation.
    /// </summary>
    public static WebApplicationBuilder AddAppConfiguration(
        this WebApplicationBuilder builder)
    {
        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile(
                $"appsettings.{builder.Environment.EnvironmentName}.json",
                optional: true,
                reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services
            .AddOptions<CoreWcfOptions>()
            .Bind(builder.Configuration.GetSection(CoreWcfOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();     // fails at startup if config is missing/invalid

        return builder;
    }
}

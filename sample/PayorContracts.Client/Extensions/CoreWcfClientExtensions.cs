using System.ServiceModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayorContracts.Client.Configuration;

namespace PayorContracts.Client.Extensions;

/// <summary>
/// Extension methods for registering CoreWCF client services in the DI container.
/// Consumed by both PayorContracts.WebApi and PayorContracts.Scheduler.
/// </summary>
public static class CoreWcfClientExtensions
{
    /// <summary>
    /// Binds and validates the "CoreWcfClient" configuration section from appsettings.json.
    /// Must be called once in Program.cs before <see cref="AddCoreWcfClient{TContract}"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCoreWcfClientConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<CoreWcfClientOptions>()
            .Bind(configuration.GetSection(CoreWcfClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers a typed CoreWCF client channel as a singleton in the DI container.
    /// Creates a ChannelFactory with WSHttpBinding and Windows authentication
    /// using the process/service account identity (DefaultNetworkCredentials).
    /// Replaces: manual ChannelFactory setup scattered across legacy ASMX and scheduler code.
    /// </summary>
    /// <typeparam name="TContract">The WCF service contract interface to create a client for.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCoreWcfClient<TContract>(
        this IServiceCollection services)
        where TContract : class
    {
        services.AddSingleton<TContract>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CoreWcfClientOptions>>().Value;
            var binding = BuildClientBinding(options.Binding);
            var endpoint = new EndpointAddress(options.EndpointUrl);

            var factory = new ChannelFactory<TContract>(binding, endpoint);

            // Windows auth — uses the process/service account identity automatically
            factory.Credentials.Windows.ClientCredential =
                System.Net.CredentialCache.DefaultNetworkCredentials;

            return factory.CreateChannel();
        });

        return services;
    }

    /// <summary>
    /// Builds a WSHttpBinding configured from client appsettings values.
    /// Security mode and credential type must match the server configuration.
    /// </summary>
    /// <param name="bindingOptions">Client binding options from appsettings.</param>
    /// <returns>A configured WSHttpBinding instance.</returns>
    private static WSHttpBinding BuildClientBinding(ClientBindingOptions bindingOptions)
    {
        var securityMode = Enum.Parse<SecurityMode>(bindingOptions.Security.Mode);

        var binding = new WSHttpBinding(securityMode)
        {
            MaxReceivedMessageSize = bindingOptions.MaxReceivedMessageSize,
            ReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas
            {
                MaxDepth                  = bindingOptions.ReaderQuotas.MaxDepth,
                MaxStringContentLength    = bindingOptions.ReaderQuotas.MaxStringContentLength,
                MaxArrayLength            = bindingOptions.ReaderQuotas.MaxArrayLength,
                MaxBytesPerRead           = bindingOptions.ReaderQuotas.MaxBytesPerRead,
                MaxNameTableCharCount     = bindingOptions.ReaderQuotas.MaxNameTableCharCount
            }
        };

        binding.Security.Message.ClientCredentialType =
            Enum.Parse<MessageCredentialType>(bindingOptions.Security.ClientCredentialType);

        return binding;
    }
}

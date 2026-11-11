using CoreWCF;
using CoreWCF.Configuration;
using CoreWCF.Channels;
using CoreWCF.Description;
using Microsoft.Extensions.Options;
using PayorContracts.Host.Configuration;
using PayorContracts.Host.Logging;
using PayorContracts.Host.Services;
using PayorContracts.Host.Contracts;

namespace PayorContracts.Host.Extensions;

public static class CoreWcfExtensions
{
    /// <summary>Registers CoreWCF services and metadata in the DI container.</summary>
    public static WebApplicationBuilder AddCoreWcfServices(
        this WebApplicationBuilder builder)
    {
        builder.Services.AddServiceModelServices();
        builder.Services.AddServiceModelMetadata();

        return builder;
    }

    /// <summary>
    /// Configures CoreWCF endpoints, bindings, and behaviors from IOptions&lt;CoreWcfOptions&gt;.
    /// Replaces: &lt;services&gt;, &lt;bindings&gt;, &lt;behaviors&gt; in web.config.
    /// </summary>
    public static WebApplication UseCoreWcfEndpoints(this WebApplication app)
    {
        var options = app.Services
            .GetRequiredService<IOptions<CoreWcfOptions>>().Value;

        // ServiceMetadata — registered by AddServiceModelMetadata() and applied to all services by CoreWCF.
        // Replaces: <serviceMetadata httpGetEnabled="true"/> in web.config.
        var metadataBehavior = app.Services
            .GetRequiredService<ServiceMetadataBehavior>();
        metadataBehavior.HttpGetEnabled = options.ServiceBehavior.HttpGetEnabled;

        var binding = BuildWsHttpBinding(options.Binding);

        // Resolve the message logging behavior from DI (null if logging disabled)
        SoapMessageLoggingBehavior? loggingBehavior = null;
        if (options.MessageLogging.Enabled)
        {
            var inspectorLogger = app.Services
                .GetRequiredService<ILogger<SoapMessageLoggingInspector>>();
            loggingBehavior = new SoapMessageLoggingBehavior(inspectorLogger);
        }

        app.UseServiceModel(serviceBuilder =>
        {
            // Register all services using the shared helper method.
            // To add a new service, add one line here:
            //   serviceBuilder.RegisterService<NewService, INewContract>(options, binding, "/NewService.svc", loggingBehavior);
            serviceBuilder.RegisterService<PayorInformation, IPayorInformation>(
                options, binding, "/PayorInformation.svc", loggingBehavior);

            serviceBuilder.RegisterService<ContractHierarchy, IContractHierarchy>(
                options, binding, "/ContractHierarchy.svc", loggingBehavior);
        });

        return app;
    }

    /// <summary>
    /// Registers a CoreWCF service with standard behaviors, endpoint, and MaxItemsInObjectGraph.
    /// Call once per service — all default behavior settings from appsettings are applied automatically.
    /// </summary>
    /// <typeparam name="TService">The service implementation class.</typeparam>
    /// <typeparam name="TContract">The service contract interface.</typeparam>
    /// <param name="serviceBuilder">The CoreWCF service builder.</param>
    /// <param name="options">CoreWCF options from appsettings.</param>
    /// <param name="binding">The pre-built WSHttpBinding.</param>
    /// <param name="endpointPath">The URL path for the service endpoint (e.g., "/PayorInformation.svc").</param>
    /// <param name="loggingBehavior">Optional SOAP message logging behavior (null = disabled).</param>
    private static void RegisterService<TService, TContract>(
        this IServiceBuilder serviceBuilder,
        CoreWcfOptions options,
        Binding binding,
        string endpointPath,
        SoapMessageLoggingBehavior? loggingBehavior)
        where TService : class
        where TContract : class
    {
        // Service registration with debug behavior from appsettings
        serviceBuilder.AddService<TService>(serviceOptions =>
        {
            serviceOptions.DebugBehavior.IncludeExceptionDetailInFaults =
                options.ServiceBehavior.IncludeExceptionDetailInFaults;
        });

        // Service endpoint
        serviceBuilder.AddServiceEndpoint<TService, TContract>(binding, endpointPath);

        // Configure service host: MaxItemsInObjectGraph + optional SOAP message logging
        serviceBuilder.ConfigureServiceHostBase<TService>(host =>
        {
            // Replaces: <dataContractSerializer maxItemsInObjectGraph="2147483647"/> in web.config.
            ApplyMaxItemsInObjectGraph(host, options.ServiceBehavior.MaxItemsInObjectGraph);

            // Replaces: <diagnostics><messageLogging> in web.config.
            // Conditionally attaches IDispatchMessageInspector for SOAP-level logging.
            if (loggingBehavior != null)
            {
                host.Description.Behaviors.Add(loggingBehavior);
            }
        });
    }

    /// <summary>
    /// Sets MaxItemsInObjectGraph on the DataContractSerializerOperationBehavior
    /// for every operation across all endpoints of the service host.
    /// Replaces: &lt;dataContractSerializer maxItemsInObjectGraph="2147483647"/&gt; in web.config
    /// (both serviceBehavior and endpointBehavior levels).
    /// </summary>
    private static void ApplyMaxItemsInObjectGraph(ServiceHostBase host, int maxItems)
    {
        foreach (var endpoint in host.Description.Endpoints)
        {
            foreach (var operation in endpoint.Contract.Operations)
            {
                var behavior = operation.OperationBehaviors
                    .OfType<DataContractSerializerOperationBehavior>()
                    .FirstOrDefault();

                if (behavior != null)
                {
                    behavior.MaxItemsInObjectGraph = maxItems;
                }
            }
        }
    }

    /// <summary>
    /// Builds a binding from appsettings values.
    /// For SecurityMode.None (dev): uses BasicHttpBinding (fully supported in CoreWCF).
    /// For SecurityMode.Transport (prod): uses BasicHttpBinding with Transport security (Windows Auth via HTTPS).
    /// CoreWCF does not support WSHttpBinding with SecurityMode.Message — BasicHttpBinding with Transport
    /// is the supported alternative that provides the same Windows Authentication (Kerberos/NTLM).
    /// Replaces: &lt;wsHttpBinding&gt;&lt;binding&gt;&lt;readerQuotas&gt; in web.config.
    /// </summary>
    private static Binding BuildWsHttpBinding(BindingOptions bindingOptions)
    {
        var securityMode = bindingOptions.Security.Mode;

        if (string.Equals(securityMode, "None", StringComparison.OrdinalIgnoreCase))
        {
            // No security — used for local development without domain infrastructure
            return new BasicHttpBinding(BasicHttpSecurityMode.None)
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
        }

        // Transport security — Windows Auth via HTTPS (Kerberos/NTLM Negotiate header)
        var binding = new BasicHttpBinding(BasicHttpSecurityMode.Transport)
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

        binding.Security.Transport.ClientCredentialType =
            Enum.Parse<HttpClientCredentialType>(bindingOptions.Security.ClientCredentialType);

        return binding;
    }
}

using CoreWCF;
using CoreWCF.Channels;
using CoreWCF.Description;
using CoreWCF.Dispatcher;

namespace PayorContracts.Host.Logging;

/// <summary>
/// Service behavior that attaches the SoapMessageLoggingInspector to all endpoints.
/// Registered via ConfigureServiceHostBase when MessageLogging.Enabled = true.
/// </summary>
public class SoapMessageLoggingBehavior : IServiceBehavior
{
    private readonly ILogger<SoapMessageLoggingInspector> _logger;

    public SoapMessageLoggingBehavior(ILogger<SoapMessageLoggingInspector> logger)
    {
        _logger = logger;
    }

    public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) { }

    public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase,
        System.Collections.ObjectModel.Collection<ServiceEndpoint> endpoints,
        BindingParameterCollection bindingParameters) { }

    public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
    {
        var inspector = new SoapMessageLoggingInspector(_logger);

        foreach (var channelDispatcher in serviceHostBase.ChannelDispatchers.OfType<ChannelDispatcher>())
        {
            foreach (var endpointDispatcher in channelDispatcher.Endpoints)
            {
                endpointDispatcher.DispatchRuntime.MessageInspectors.Add(inspector);
            }
        }
    }
}

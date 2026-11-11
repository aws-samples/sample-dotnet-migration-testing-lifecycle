using CoreWCF;
using CoreWCF.Channels;
using CoreWCF.Dispatcher;

namespace PayorContracts.Host.Logging;

/// <summary>
/// CoreWCF message inspector that logs SOAP request and response messages.
/// Replaces: &lt;diagnostics&gt;&lt;messageLogging logEntireMessage="true"/&gt; from web.config.
/// Operates at the WCF message layer — sees decrypted SOAP content even with SecurityMode.Message.
/// </summary>
/// <remarks>
/// Logs full SOAP envelopes including personal data in the payload (contact email, phone)
/// — local debugging only; redact contact fields before enabling anywhere real data flows.
/// Disabled by default in every appsettings file; see CoreWcf:MessageLogging:Enabled.
/// For a production-shaped equivalent, log only <see cref="Message.Headers"/> Action, the
/// message size and a correlation id.
/// </remarks>
public class SoapMessageLoggingInspector : IDispatchMessageInspector
{
    private readonly ILogger<SoapMessageLoggingInspector> _logger;

    public SoapMessageLoggingInspector(ILogger<SoapMessageLoggingInspector> logger)
    {
        _logger = logger;
    }

    public object? AfterReceiveRequest(ref Message request, IClientChannel channel, InstanceContext instanceContext)
    {
        // Create a buffered copy so the message can still be processed after reading
        using var buffer = request.CreateBufferedCopy(int.MaxValue);
        var copy = buffer.CreateMessage();

        _logger.LogDebug("SOAP Request [Action: {Action}]\n{Message}",
            copy.Headers.Action, copy);

        // Replace the original message with a fresh copy from the buffer
        request = buffer.CreateMessage();

        return null;
    }

    public void BeforeSendReply(ref Message reply, object? correlationState)
    {
        if (reply == null) return;

        using var buffer = reply.CreateBufferedCopy(int.MaxValue);
        var copy = buffer.CreateMessage();

        _logger.LogDebug("SOAP Response [Action: {Action}]\n{Message}",
            copy.Headers.Action, copy);

        reply = buffer.CreateMessage();
    }
}

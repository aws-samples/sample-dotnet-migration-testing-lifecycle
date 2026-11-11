using System.ComponentModel.DataAnnotations;

namespace PayorContracts.Host.Configuration;

/// <summary>
/// Root configuration options for the CoreWCF server host.
/// Bound from the "CoreWcf" section in appsettings.json.
/// Validated at startup via DataAnnotations and ValidateOnStart().
/// </summary>
public class CoreWcfOptions
{
    /// <summary>Configuration section name in appsettings.json.</summary>
    public const string SectionName = "CoreWcf";

    /// <summary>WSHttpBinding configuration (message size limits, reader quotas, security).</summary>
    [Required] public BindingOptions Binding { get; set; } = new();

    /// <summary>Service-level behavior settings (exception details, metadata, serialization limits).</summary>
    [Required] public ServiceBehaviorOptions ServiceBehavior { get; set; } = new();

    /// <summary>SOAP message logging configuration (enabled/disabled, log level).</summary>
    [Required] public MessageLoggingOptions MessageLogging { get; set; } = new();
}

/// <summary>
/// WSHttpBinding configuration options.
/// Replaces: &lt;wsHttpBinding&gt;&lt;binding&gt; in web.config.
/// </summary>
public class BindingOptions
{
    /// <summary>
    /// Maximum size of incoming messages in bytes.
    /// Replaces: maxReceivedMessageSize="2147483647" in web.config.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue, ErrorMessage = "MaxReceivedMessageSize must be greater than 0")]
    public long MaxReceivedMessageSize { get; set; }

    /// <summary>XML reader quota limits for deserialization.</summary>
    [Required] public ReaderQuotaOptions ReaderQuotas { get; set; } = new();

    /// <summary>Binding security configuration (mode and credential type).</summary>
    [Required] public SecurityOptions Security { get; set; } = new();
}

/// <summary>
/// XML reader quota options controlling deserialization limits.
/// Replaces: &lt;readerQuotas&gt; in web.config.
/// </summary>
public class ReaderQuotaOptions
{
    /// <summary>Maximum XML element nesting depth.</summary>
    [Range(1, int.MaxValue)] public int MaxDepth { get; set; }

    /// <summary>Maximum string length allowed in XML content.</summary>
    [Range(1, int.MaxValue)] public int MaxStringContentLength { get; set; }

    /// <summary>Maximum array/collection length allowed during deserialization.</summary>
    [Range(1, int.MaxValue)] public int MaxArrayLength { get; set; }

    /// <summary>Maximum bytes read per XML read operation.</summary>
    [Range(1, int.MaxValue)] public int MaxBytesPerRead { get; set; }

    /// <summary>Maximum XML name table character count.</summary>
    [Range(1, int.MaxValue)] public int MaxNameTableCharCount { get; set; }
}

/// <summary>
/// WSHttpBinding security configuration.
/// Replaces: &lt;security mode="Message"&gt;&lt;message clientCredentialType="Windows"/&gt; in web.config.
/// </summary>
public class SecurityOptions
{
    /// <summary>
    /// WCF security mode: Message, Transport, or TransportWithMessageCredential.
    /// WSHttpBinding defaults to Message.
    /// </summary>
    [Required(ErrorMessage = "Security.Mode is required (Message, Transport, or TransportWithMessageCredential)")]
    public string Mode { get; set; } = default!;

    /// <summary>
    /// Client credential type: Windows, Certificate, or None.
    /// Windows uses Kerberos/NTLM via Active Directory.
    /// </summary>
    [Required(ErrorMessage = "Security.ClientCredentialType is required (Windows, Certificate, or None)")]
    public string ClientCredentialType { get; set; } = default!;
}

/// <summary>
/// Service behavior options applied to all registered CoreWCF services.
/// Replaces: &lt;serviceBehaviors&gt;&lt;behavior&gt; in web.config.
/// </summary>
public class ServiceBehaviorOptions
{
    /// <summary>
    /// Whether to include full exception details (stack trace) in SOAP fault responses.
    /// Should be true in Development, false in Production.
    /// Replaces: &lt;serviceDebug includeExceptionDetailInFaults="true"/&gt;.
    /// </summary>
    public bool IncludeExceptionDetailInFaults { get; set; }

    /// <summary>
    /// Whether WSDL metadata is available via HTTP GET (at ?wsdl URL).
    /// Replaces: &lt;serviceMetadata httpGetEnabled="true"/&gt;.
    /// </summary>
    public bool HttpGetEnabled { get; set; }

    /// <summary>
    /// Maximum number of items in an object graph to serialize/deserialize.
    /// Replaces: &lt;dataContractSerializer maxItemsInObjectGraph="2147483647"/&gt;.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "MaxItemsInObjectGraph must be greater than 0")]
    public int MaxItemsInObjectGraph { get; set; }
}

/// <summary>
/// SOAP message logging configuration.
/// Replaces: &lt;diagnostics&gt;&lt;messageLogging&gt; in web.config.
/// When enabled, attaches an IDispatchMessageInspector to log request/response SOAP messages.
/// </summary>
public class MessageLoggingOptions
{
    /// <summary>
    /// Whether SOAP message logging is active. Off in every environment by default: the
    /// inspector logs whole SOAP envelopes, including personal data in the payload.
    /// Turn it on only for local debugging against non-real data.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Log level for SOAP messages (e.g., Debug, Warning, Error).</summary>
    [Required(ErrorMessage = "MessageLogging.LogLevel is required")]
    public string LogLevel { get; set; } = default!;
}

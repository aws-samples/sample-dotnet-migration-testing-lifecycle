using System.ComponentModel.DataAnnotations;

namespace PayorContracts.Client.Configuration;

/// <summary>
/// Client-side configuration options for connecting to CoreWCF services.
/// Bound from the "CoreWcfClient" section in appsettings.json.
/// Validated at startup via DataAnnotations and ValidateOnStart().
/// </summary>
public class CoreWcfClientOptions
{
    /// <summary>Configuration section name in appsettings.json.</summary>
    public const string SectionName = "CoreWcfClient";

    /// <summary>
    /// The full URL of the CoreWCF service endpoint.
    /// Example: "http://corewcf-server/PayorInformation.svc"
    /// </summary>
    [Required(ErrorMessage = "EndpointUrl is required")]
    [Url(ErrorMessage = "EndpointUrl must be a valid URL")]
    public string EndpointUrl { get; set; } = default!;

    /// <summary>Client-side binding configuration (must match server binding settings).</summary>
    [Required] public ClientBindingOptions Binding { get; set; } = new();
}

/// <summary>
/// Client-side WSHttpBinding configuration.
/// Values should match the server's binding configuration.
/// </summary>
public class ClientBindingOptions
{
    /// <summary>Maximum size of incoming response messages in bytes.</summary>
    [Required][Range(1, long.MaxValue)] public long MaxReceivedMessageSize { get; set; }

    /// <summary>XML reader quota limits for response deserialization.</summary>
    [Required] public ClientReaderQuotaOptions ReaderQuotas { get; set; } = new();

    /// <summary>Binding security configuration (must match server).</summary>
    [Required] public ClientSecurityOptions Security { get; set; } = new();
}

/// <summary>
/// Client-side XML reader quota options.
/// Should match the server's ReaderQuotas configuration.
/// </summary>
public class ClientReaderQuotaOptions
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
/// Client-side security options.
/// Must match the server's security mode and credential type.
/// </summary>
public class ClientSecurityOptions
{
    /// <summary>WCF security mode: Message, Transport, or TransportWithMessageCredential.</summary>
    [Required(ErrorMessage = "Security.Mode is required")]
    public string Mode { get; set; } = default!;

    /// <summary>Client credential type: Windows, Certificate, or None.</summary>
    [Required(ErrorMessage = "Security.ClientCredentialType is required")]
    public string ClientCredentialType { get; set; } = default!;
}

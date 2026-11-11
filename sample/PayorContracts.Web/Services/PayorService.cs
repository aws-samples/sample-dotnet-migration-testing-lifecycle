using System.ServiceModel;
using PayorContracts.Web.Models;

namespace PayorContracts.Web.Services;

/// <summary>
/// Service class that calls CoreWCF via WCF ChannelFactory.
/// Injected directly into Blazor components via DI.
/// </summary>
public class PayorService : IDisposable
{
    private readonly ChannelFactory<IPayorInformation> _factory;
    private readonly ILogger<PayorService> _logger;
    private readonly PayorAccessGuard _accessGuard;
    private bool _disposed;

    public PayorService(IConfiguration configuration, PayorAccessGuard accessGuard, ILogger<PayorService> logger)
    {
        var endpointUrl = configuration["WcfService:EndpointUrl"]
            ?? throw new InvalidOperationException("WcfService:EndpointUrl is not configured.");
        _logger = logger;
        _accessGuard = accessGuard;

        // One factory per service instance (scoped), reused for every call and released in Dispose
        var binding = new BasicHttpBinding(BasicHttpSecurityMode.None)
        {
            MaxReceivedMessageSize = int.MaxValue
        };
        _factory = new ChannelFactory<IPayorInformation>(binding, new EndpointAddress(endpointUrl));
    }

    private IPayorInformation CreateChannel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _factory.CreateChannel();
    }

    /// <summary>
    /// Close-or-Abort: a faulted channel is aborted, and a failing Close falls back to Abort,
    /// so the original service error is never masked by CommunicationObjectFaultedException.
    /// </summary>
    private static void CloseOrAbort(ICommunicationObject communicationObject)
    {
        if (communicationObject.State == CommunicationState.Faulted)
        {
            communicationObject.Abort();
            return;
        }

        try
        {
            communicationObject.Close();
        }
        catch (Exception ex) when (ex is CommunicationException or TimeoutException)
        {
            communicationObject.Abort();
        }
    }

    /// <summary>Closes (or aborts) the shared ChannelFactory.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseOrAbort(_factory);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets all payors from the CoreWCF service.
    /// </summary>
    public List<PayorDto> GetAllPayors()
    {
        _logger.LogInformation("Calling CoreWCF GetAllPayors");
        var channel = CreateChannel();

        try
        {
            var response = channel.GetAllPayors();
            return response.Payors ?? new List<PayorDto>();
        }
        finally
        {
            CloseOrAbort((IClientChannel)channel);
        }
    }

    /// <summary>
    /// Filters, sorts and pages payors (Reference ASMX GetPayors): name/code contains
    /// (case-insensitive), optional status filter, sort by column (default Id ascending),
    /// page size default 10.
    /// </summary>
    public async Task<PayorSearchResult> SearchPayorsAsync(PayorSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var all = await Task.Run(GetAllPayors, cancellationToken);
        return ApplySearch(all, request);
    }

    /// <summary>
    /// Batch updates payors without blocking the render thread (Save All).
    /// The service response, including its Message, is returned unchanged.
    /// Guarded: throws <see cref="UnauthorizedAccessException"/> when the user may not modify payors.
    /// </summary>
    public async Task<BatchUpdatePayorResponse> BatchUpdatePayorsAsync(List<UpdatePayorRequest> payors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payors);
        await _accessGuard.EnforceModifyAccessAsync("update payors");
        return await Task.Run(() => SendBatchUpdate(payors), cancellationToken);
    }

    /// <summary>
    /// In-memory filter/sort/page, identical to the Reference GetPayors controller logic.
    /// </summary>
    public static PayorSearchResult ApplySearch(IEnumerable<PayorDto> payors, PayorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(payors);
        ArgumentNullException.ThrowIfNull(request);

        var filtered = payors;

        if (!string.IsNullOrWhiteSpace(request.NameFilter))
            filtered = filtered.Where(p => p.Name.Contains(request.NameFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.CodeFilter))
            filtered = filtered.Where(p => p.Code.Contains(request.CodeFilter, StringComparison.OrdinalIgnoreCase));

        if (request.ActiveOnly.HasValue)
            filtered = filtered.Where(p => p.IsActive == request.ActiveOnly.Value);

        var desc = request.SortDescending;
        filtered = (request.SortColumn ?? string.Empty).ToLowerInvariant() switch
        {
            "name" => desc ? filtered.OrderByDescending(p => p.Name) : filtered.OrderBy(p => p.Name),
            "code" => desc ? filtered.OrderByDescending(p => p.Code) : filtered.OrderBy(p => p.Code),
            "isactive" => desc ? filtered.OrderByDescending(p => p.IsActive) : filtered.OrderBy(p => p.IsActive),
            "category" => desc ? filtered.OrderByDescending(p => p.Category) : filtered.OrderBy(p => p.Category),
            "effectivedate" => desc ? filtered.OrderByDescending(p => p.EffectiveDate) : filtered.OrderBy(p => p.EffectiveDate),
            "id" => desc ? filtered.OrderByDescending(p => p.Id) : filtered.OrderBy(p => p.Id),
            _ => filtered.OrderBy(p => p.Id)
        };

        var list = filtered.ToList();
        var pageSize = request.PageSize > 0 ? request.PageSize : PayorSearchRequest.DefaultPageSize;
        var page = request.Page > 0 ? request.Page : 1;

        return new PayorSearchResult
        {
            Data = list.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalRecords = list.Count,
            TotalPages = (int)Math.Ceiling((double)list.Count / pageSize),
            CurrentPage = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Updates a single payor record via the CoreWCF service.
    /// Guarded: throws <see cref="UnauthorizedAccessException"/> when the user may not modify payors.
    /// </summary>
    public async Task<UpdatePayorResponse> UpdatePayorAsync(UpdatePayorRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _accessGuard.EnforceModifyAccessAsync("update payors");
        return await Task.Run(() => SendUpdate(request), cancellationToken);
    }

    /// <summary>WCF call of UpdatePayor; only reached through the guarded <see cref="UpdatePayorAsync"/>.</summary>
    private UpdatePayorResponse SendUpdate(UpdatePayorRequest request)
    {
        _logger.LogInformation("Calling CoreWCF UpdatePayor for Id: {Id}", request.Id);
        var channel = CreateChannel();

        try
        {
            return channel.UpdatePayor(request);
        }
        finally
        {
            CloseOrAbort((IClientChannel)channel);
        }
    }

    /// <summary>
    /// WCF call of BatchUpdatePayors; only reached through the guarded <see cref="BatchUpdatePayorsAsync"/>.
    /// </summary>
    private BatchUpdatePayorResponse SendBatchUpdate(List<UpdatePayorRequest> payors)
    {
        _logger.LogInformation("Calling CoreWCF BatchUpdatePayors with {Count} items", payors.Count);
        var channel = CreateChannel();

        try
        {
            var request = new BatchUpdatePayorRequest { Payors = payors };
            return channel.BatchUpdatePayors(request);
        }
        finally
        {
            CloseOrAbort((IClientChannel)channel);
        }
    }
}

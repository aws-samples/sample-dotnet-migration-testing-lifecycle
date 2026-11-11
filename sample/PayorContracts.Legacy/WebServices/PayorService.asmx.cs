using Microsoft.AspNetCore.Mvc;
using PayorContracts.Legacy.Models;

namespace PayorContracts.Legacy.WebServices;

/// <summary>
/// Simulates the legacy ASMX web service (PayorService.asmx).
/// Keeps the same URL pattern: /WebServices/PayorService.asmx/GetPayors
/// Called via jQuery AJAX from PayorList.aspx.
/// Internally calls CoreWCF service via WCF ChannelFactory.
/// </summary>
[Route("WebServices/PayorService.asmx")]
[ApiController]
public class PayorServiceController : ControllerBase
{
    private readonly PayorServiceClient _wcfClient;

    public PayorServiceController(PayorServiceClient wcfClient)
    {
        _wcfClient = wcfClient;
    }

    /// <summary>
    /// Gets payors with filtering, sorting, and pagination.
    /// </summary>
    [HttpPost("GetPayors")]
    public ActionResult GetPayors([FromBody] PayorSearchRequest request)
    {
        var allPayors = _wcfClient.GetAllPayors();
        var filtered = allPayors.Payors.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.NameFilter))
            filtered = filtered.Where(p => p.Name.Contains(request.NameFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.CodeFilter))
            filtered = filtered.Where(p => p.Code.Contains(request.CodeFilter, StringComparison.OrdinalIgnoreCase));

        if (request.ActiveOnly.HasValue)
            filtered = filtered.Where(p => p.IsActive == request.ActiveOnly.Value);

        filtered = (request.SortColumn ?? "").ToLower() switch
        {
            "name" => request.SortDirection == "desc" ? filtered.OrderByDescending(p => p.Name) : filtered.OrderBy(p => p.Name),
            "code" => request.SortDirection == "desc" ? filtered.OrderByDescending(p => p.Code) : filtered.OrderBy(p => p.Code),
            "isactive" => request.SortDirection == "desc" ? filtered.OrderByDescending(p => p.IsActive) : filtered.OrderBy(p => p.IsActive),
            "category" => request.SortDirection == "desc" ? filtered.OrderByDescending(p => p.Category) : filtered.OrderBy(p => p.Category),
            "effectivedate" => request.SortDirection == "desc" ? filtered.OrderByDescending(p => p.EffectiveDate) : filtered.OrderBy(p => p.EffectiveDate),
            _ => filtered.OrderBy(p => p.Id)
        };

        var totalRecords = filtered.Count();
        var pageSize = request.PageSize > 0 ? request.PageSize : 10;
        var page = request.Page > 0 ? request.Page : 1;
        var pagedData = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new
        {
            Data = pagedData,
            TotalRecords = totalRecords,
            TotalPages = (int)Math.Ceiling((double)totalRecords / pageSize),
            CurrentPage = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Gets a single payor by ID.
    /// </summary>
    [HttpPost("GetPayorById")]
    public ActionResult GetPayorById([FromBody] PayorByIdRequest request)
    {
        var result = _wcfClient.GetPayorById(request.Id);
        return Ok(result);
    }

    /// <summary>
    /// Updates a single payor record.
    /// </summary>
    [HttpPost("UpdatePayor")]
    public ActionResult UpdatePayor([FromBody] UpdatePayorApiRequest request)
    {
        var wcfRequest = new UpdatePayorRequest
        {
            Id = request.Id,
            Name = request.Name,
            Code = request.Code,
            IsActive = request.IsActive,
            Category = request.Category,
            EffectiveDate = request.EffectiveDate
        };
        var result = _wcfClient.UpdatePayor(wcfRequest);
        return Ok(result);
    }

    /// <summary>
    /// Batch updates multiple payor records.
    /// Called from the Save All button in the grid.
    /// </summary>
    [HttpPost("BatchUpdatePayors")]
    public ActionResult BatchUpdatePayors([FromBody] BatchUpdateApiRequest request)
    {
        var wcfRequest = new BatchUpdatePayorRequest
        {
            Payors = request.Payors.Select(p => new UpdatePayorRequest
            {
                Id = p.Id,
                Name = p.Name,
                Code = p.Code,
                IsActive = p.IsActive,
                Category = p.Category,
                EffectiveDate = p.EffectiveDate
            }).ToList()
        };
        var result = _wcfClient.BatchUpdatePayors(wcfRequest);
        return Ok(result);
    }
}

public class PayorSearchRequest
{
    public string? NameFilter { get; set; }
    public string? CodeFilter { get; set; }
    public bool? ActiveOnly { get; set; }
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PayorByIdRequest
{
    public int Id { get; set; }
}

public class UpdatePayorApiRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime? EffectiveDate { get; set; }
}

public class BatchUpdateApiRequest
{
    public List<UpdatePayorApiRequest> Payors { get; set; } = new();
}

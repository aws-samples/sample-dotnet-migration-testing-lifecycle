namespace PayorContracts.Web.Models;

/// <summary>
/// Status filter of the PayorList page (Reference ddlActiveFilter: All / Active / Inactive).
/// </summary>
public enum PayorStatusFilter
{
    All,
    Active,
    Inactive
}

/// <summary>
/// Search, sort and paging request for the payor grid.
/// Mirrors the Reference ASMX PayorSearchRequest (GetPayors).
/// </summary>
public sealed class PayorSearchRequest
{
    public const int DefaultPageSize = 10;

    public string? NameFilter { get; init; }
    public string? CodeFilter { get; init; }

    /// <summary>null = all statuses, true = active only, false = inactive only.</summary>
    public bool? ActiveOnly { get; init; }

    /// <summary>Column name (id, name, code, category, effectiveDate, isActive). Unknown or empty sorts by Id.</summary>
    public string? SortColumn { get; init; }

    public bool SortDescending { get; init; }

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

/// <summary>
/// One page of payors plus paging metadata (Reference GetPayors response).
/// </summary>
public sealed class PayorSearchResult
{
    public IReadOnlyList<PayorDto> Data { get; init; } = Array.Empty<PayorDto>();
    public int TotalRecords { get; init; }
    public int TotalPages { get; init; }
    public int CurrentPage { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Editable grid row of the PayorList page. Holds the current (possibly edited) values of a payor.
/// </summary>
public sealed class PayorGridRow
{
    public int Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime? EffectiveDate { get; set; }
    public bool IsActive { get; set; }

    public static PayorGridRow FromDto(PayorDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new PayorGridRow
        {
            Id = dto.Id,
            Name = dto.Name,
            Code = dto.Code,
            Category = dto.Category,
            EffectiveDate = dto.EffectiveDate,
            IsActive = dto.IsActive
        };
    }

    public UpdatePayorRequest ToUpdateRequest() => new()
    {
        Id = Id,
        Name = Name,
        Code = Code,
        Category = Category,
        EffectiveDate = EffectiveDate,
        IsActive = IsActive
    };
}

using CoreWCF;
using PayorContracts.Host.Contracts;

namespace PayorContracts.Host.Services;

/// <summary>
/// CoreWCF service implementation for payor information operations.
/// Returns sample data for development and integration testing.
/// </summary>
[ServiceBehavior(IncludeExceptionDetailInFaults = false)]
public class PayorInformation : IPayorInformation
{
    private readonly ILogger<PayorInformation> _logger;
    private readonly string _connectionString;
    private readonly string _ssrsReportServerUrl;
    private readonly int _maxRetryAttempts;

    // Sample data simulating database records
    private static readonly List<PayorDto> SamplePayors = new()
    {
        new PayorDto { Id = 1, Name = "Acme Health Plans", Code = "ACM001", IsActive = true, Category = "Commercial", EffectiveDate = new DateTime(2023, 1, 15) },
        new PayorDto { Id = 2, Name = "Bluebird Health Shield", Code = "BBHS01", IsActive = true, Category = "Commercial", EffectiveDate = new DateTime(2022, 6, 1) },
        new PayorDto { Id = 3, Name = "Citrine Valley Healthcare", Code = "CTR001", IsActive = true, Category = "Commercial", EffectiveDate = new DateTime(2023, 3, 1) },
        new PayorDto { Id = 4, Name = "Unity Crest Healthcare", Code = "UCH001", IsActive = true, Category = "Commercial", EffectiveDate = new DateTime(2022, 9, 15) },
        new PayorDto { Id = 5, Name = "Hollowbrook Health Inc", Code = "HBR001", IsActive = true, Category = "Commercial", EffectiveDate = new DateTime(2024, 1, 1) },
        new PayorDto { Id = 6, Name = "Kestrel Ridge Health", Code = "KST001", IsActive = true, Category = "Commercial", EffectiveDate = new DateTime(2023, 7, 1) },
        new PayorDto { Id = 7, Name = "Moonstone Healthcare", Code = "MOO001", IsActive = true, Category = "Medicaid", EffectiveDate = new DateTime(2023, 4, 1) },
        new PayorDto { Id = 8, Name = "Cedarvale Care Corporation", Code = "CDR001", IsActive = false, Category = "Medicaid", EffectiveDate = new DateTime(2021, 12, 1) },
        new PayorDto { Id = 9, Name = "Willowbend Health Plans", Code = "WLW001", IsActive = true, Category = "Medicaid", EffectiveDate = new DateTime(2023, 10, 1) },
        new PayorDto { Id = 10, Name = "Apex Health Inc", Code = "APX001", IsActive = true, Category = "Commercial", EffectiveDate = new DateTime(2024, 2, 1) },
        new PayorDto { Id = 11, Name = "Maplewood Health", Code = "MAP001", IsActive = false, Category = "Commercial", EffectiveDate = new DateTime(2020, 5, 1) },
        new PayorDto { Id = 12, Name = "Tamarack Federal Plan", Code = "TMK001", IsActive = true, Category = "Tricare", EffectiveDate = new DateTime(2022, 1, 1) },
        new PayorDto { Id = 13, Name = "Mesa Senior Plan A", Code = "MESA01", IsActive = true, Category = "Medicare", EffectiveDate = new DateTime(2022, 1, 1) },
        new PayorDto { Id = 14, Name = "Mesa Senior Plan B", Code = "MESB01", IsActive = true, Category = "Medicare", EffectiveDate = new DateTime(2022, 1, 1) },
        new PayorDto { Id = 15, Name = "Meadowlark State Plan", Code = "MDW001", IsActive = true, Category = "Medicaid", EffectiveDate = new DateTime(2023, 1, 1) },
    };

    public PayorInformation(ILogger<PayorInformation> logger, IConfiguration configuration)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("PayorContractsDatabase")
            ?? throw new InvalidOperationException("ConnectionStrings:PayorContractsDatabase is not configured.");

        _ssrsReportServerUrl = configuration["AppSettings:SSRSReportServerUrl"]
            ?? throw new InvalidOperationException("AppSettings:SSRSReportServerUrl is not configured.");

        _maxRetryAttempts = configuration.GetValue<int>("AppSettings:MaxRetryAttempts");
    }

    /// <inheritdoc/>
    public PayorListResponse GetAllPayors()
    {
        _logger.LogInformation("GetAllPayors called — returning {Count} sample records", SamplePayors.Count);
        return new PayorListResponse { Payors = SamplePayors, Count = SamplePayors.Count };
    }

    /// <inheritdoc/>
    public PayorDetailResponse GetPayorById(int id)
    {
        _logger.LogInformation("GetPayorById called with id: {Id}", id);

        var payor = SamplePayors.FirstOrDefault(p => p.Id == id);
        if (payor == null)
        {
            return new PayorDetailResponse();
        }

        return new PayorDetailResponse
        {
            Id = payor.Id,
            Name = payor.Name,
            Code = payor.Code,
            IsActive = payor.IsActive
        };
    }

    /// <inheritdoc/>
    public UpdatePayorResponse UpdatePayor(UpdatePayorRequest request)
    {
        _logger.LogInformation("UpdatePayor called for id: {Id}", request.Id);

        var payor = SamplePayors.FirstOrDefault(p => p.Id == request.Id);
        if (payor == null)
        {
            return new UpdatePayorResponse { Success = false, Message = $"Payor with Id {request.Id} not found." };
        }

        payor.Name = request.Name;
        payor.Code = request.Code;
        payor.IsActive = request.IsActive;
        payor.Category = request.Category;
        payor.EffectiveDate = request.EffectiveDate;

        _logger.LogInformation("Payor {Id} updated successfully", request.Id);
        return new UpdatePayorResponse { Success = true, Message = "Payor updated successfully." };
    }

    /// <inheritdoc/>
    public BatchUpdatePayorResponse BatchUpdatePayors(BatchUpdatePayorRequest request)
    {
        _logger.LogInformation("BatchUpdatePayors called with {Count} items", request.Payors.Count);

        var errors = new List<string>();
        var updatedCount = 0;

        foreach (var item in request.Payors)
        {
            var payor = SamplePayors.FirstOrDefault(p => p.Id == item.Id);
            if (payor == null)
            {
                errors.Add($"Payor with Id {item.Id} not found.");
                continue;
            }

            payor.Name = item.Name;
            payor.Code = item.Code;
            payor.IsActive = item.IsActive;
            payor.Category = item.Category;
            payor.EffectiveDate = item.EffectiveDate;
            updatedCount++;
        }

        _logger.LogInformation("Batch update completed: {Updated} updated, {Errors} errors", updatedCount, errors.Count);

        return new BatchUpdatePayorResponse
        {
            Success = errors.Count == 0,
            UpdatedCount = updatedCount,
            Message = errors.Count == 0
                ? $"Successfully updated {updatedCount} payor(s)."
                : $"Updated {updatedCount} payor(s) with {errors.Count} error(s).",
            Errors = errors
        };
    }
}

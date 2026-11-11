using CoreWCF;
using PayorContracts.Host.Contracts;

namespace PayorContracts.Host.Services;

/// <summary>
/// CoreWCF service implementation for the Contract → Plan → Benefit hierarchy.
/// Returns sample data for development and integration testing.
/// </summary>
[ServiceBehavior(IncludeExceptionDetailInFaults = false)]
public class ContractHierarchy : IContractHierarchy
{
    private readonly ILogger<ContractHierarchy> _logger;

    // Static ID counter for generating new IDs
    private static int _nextId = 100;

    // Sample Contracts
    private static readonly List<ContractDto> SampleContracts = new()
    {
        new ContractDto { Id = 1, Name = "Acme Commercial PPO", ContractType = "Commercial", StartDate = new DateTime(2023, 1, 1), EndDate = new DateTime(2025, 12, 31), IsActive = true, PlanCount = 3 },
        new ContractDto { Id = 2, Name = "Bluebird Government Plan", ContractType = "Government", StartDate = new DateTime(2022, 7, 1), EndDate = new DateTime(2024, 6, 30), IsActive = true, PlanCount = 2 },
        new ContractDto { Id = 3, Name = "Unity Crest Senior Advantage", ContractType = "Medicare", StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31), IsActive = true, PlanCount = 2 },
        new ContractDto { Id = 4, Name = "Moonstone Managed Care", ContractType = "Medicaid", StartDate = new DateTime(2023, 4, 1), EndDate = new DateTime(2025, 3, 31), IsActive = true, PlanCount = 3 },
        new ContractDto { Id = 5, Name = "Citrine Commercial HMO", ContractType = "Commercial", StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2023, 12, 31), IsActive = false, PlanCount = 2 },
    };

    // Sample Plans
    private static readonly List<PlanDto> SamplePlans = new()
    {
        // Contract 1 - Acme Commercial PPO
        new PlanDto { Id = 10, ContractId = 1, PlanName = "Acme PPO Gold", PlanCode = "ACM-PPO-G", EffectiveDate = new DateTime(2023, 1, 1), TermDate = new DateTime(2025, 12, 31), Status = "Active", BenefitCount = 3 },
        new PlanDto { Id = 11, ContractId = 1, PlanName = "Acme PPO Silver", PlanCode = "ACM-PPO-S", EffectiveDate = new DateTime(2023, 1, 1), TermDate = new DateTime(2025, 12, 31), Status = "Active", BenefitCount = 2 },
        new PlanDto { Id = 12, ContractId = 1, PlanName = "Acme PPO Bronze", PlanCode = "ACM-PPO-B", EffectiveDate = new DateTime(2024, 1, 1), TermDate = null, Status = "Pending", BenefitCount = 2 },
        // Contract 2 - Bluebird Government
        new PlanDto { Id = 20, ContractId = 2, PlanName = "Bluebird Federal Basic", PlanCode = "BBHS-FED-B", EffectiveDate = new DateTime(2022, 7, 1), TermDate = new DateTime(2024, 6, 30), Status = "Active", BenefitCount = 3 },
        new PlanDto { Id = 21, ContractId = 2, PlanName = "Bluebird Federal Plus", PlanCode = "BBHS-FED-P", EffectiveDate = new DateTime(2022, 7, 1), TermDate = new DateTime(2024, 6, 30), Status = "Active", BenefitCount = 2 },
        // Contract 3 - Unity Crest Senior
        new PlanDto { Id = 30, ContractId = 3, PlanName = "Unity Crest SA Standard", PlanCode = "UCH-SA-STD", EffectiveDate = new DateTime(2024, 1, 1), TermDate = new DateTime(2024, 12, 31), Status = "Active", BenefitCount = 3 },
        new PlanDto { Id = 31, ContractId = 3, PlanName = "Unity Crest SA Premium", PlanCode = "UCH-SA-PRM", EffectiveDate = new DateTime(2024, 1, 1), TermDate = new DateTime(2024, 12, 31), Status = "Active", BenefitCount = 2 },
        // Contract 4 - Moonstone Managed Care
        new PlanDto { Id = 40, ContractId = 4, PlanName = "Moonstone Essential Care", PlanCode = "MOO-ESS", EffectiveDate = new DateTime(2023, 4, 1), TermDate = new DateTime(2025, 3, 31), Status = "Active", BenefitCount = 3 },
        new PlanDto { Id = 41, ContractId = 4, PlanName = "Moonstone Family Plan", PlanCode = "MOO-FAM", EffectiveDate = new DateTime(2023, 4, 1), TermDate = new DateTime(2025, 3, 31), Status = "Active", BenefitCount = 2 },
        new PlanDto { Id = 42, ContractId = 4, PlanName = "Moonstone Child Plan", PlanCode = "MOO-CHILD", EffectiveDate = new DateTime(2023, 10, 1), TermDate = null, Status = "Pending", BenefitCount = 2 },
        // Contract 5 - Citrine Commercial HMO
        new PlanDto { Id = 50, ContractId = 5, PlanName = "Citrine HMO Standard", PlanCode = "CTR-HMO-S", EffectiveDate = new DateTime(2021, 1, 1), TermDate = new DateTime(2023, 12, 31), Status = "Terminated", BenefitCount = 2 },
        new PlanDto { Id = 51, ContractId = 5, PlanName = "Citrine HMO Plus", PlanCode = "CTR-HMO-P", EffectiveDate = new DateTime(2021, 1, 1), TermDate = new DateTime(2023, 12, 31), Status = "Terminated", BenefitCount = 2 },
    };

    // Sample Benefits
    private static readonly List<BenefitDto> SampleBenefits = new()
    {
        // Plan 10 - Acme PPO Gold
        new BenefitDto { Id = 101, PlanId = 10, BenefitName = "Primary Care Visit", CoverageType = "Medical", CopayAmount = 20.00m, StartDate = new DateTime(2023, 1, 1), EndDate = new DateTime(2025, 12, 31) },
        new BenefitDto { Id = 102, PlanId = 10, BenefitName = "Dental Preventive", CoverageType = "Dental", CopayAmount = 0.00m, StartDate = new DateTime(2023, 1, 1), EndDate = new DateTime(2025, 12, 31) },
        new BenefitDto { Id = 103, PlanId = 10, BenefitName = "Vision Exam", CoverageType = "Vision", CopayAmount = 10.00m, StartDate = new DateTime(2023, 1, 1), EndDate = new DateTime(2025, 12, 31) },
        // Plan 11 - Acme PPO Silver
        new BenefitDto { Id = 111, PlanId = 11, BenefitName = "Primary Care Visit", CoverageType = "Medical", CopayAmount = 35.00m, StartDate = new DateTime(2023, 1, 1), EndDate = new DateTime(2025, 12, 31) },
        new BenefitDto { Id = 112, PlanId = 11, BenefitName = "Generic Rx", CoverageType = "Pharmacy", CopayAmount = 10.00m, StartDate = new DateTime(2023, 1, 1), EndDate = new DateTime(2025, 12, 31) },
        // Plan 12 - Acme PPO Bronze
        new BenefitDto { Id = 121, PlanId = 12, BenefitName = "Urgent Care Visit", CoverageType = "Medical", CopayAmount = 75.00m, StartDate = new DateTime(2024, 1, 1), EndDate = null },
        new BenefitDto { Id = 122, PlanId = 12, BenefitName = "Behavioral Health", CoverageType = "Behavioral", CopayAmount = 40.00m, StartDate = new DateTime(2024, 1, 1), EndDate = null },
        // Plan 20 - Bluebird Federal Basic
        new BenefitDto { Id = 201, PlanId = 20, BenefitName = "Office Visit", CoverageType = "Medical", CopayAmount = 25.00m, StartDate = new DateTime(2022, 7, 1), EndDate = new DateTime(2024, 6, 30) },
        new BenefitDto { Id = 202, PlanId = 20, BenefitName = "Dental Basic", CoverageType = "Dental", CopayAmount = 15.00m, StartDate = new DateTime(2022, 7, 1), EndDate = new DateTime(2024, 6, 30) },
        new BenefitDto { Id = 203, PlanId = 20, BenefitName = "Pharmacy Tier 1", CoverageType = "Pharmacy", CopayAmount = 5.00m, StartDate = new DateTime(2022, 7, 1), EndDate = new DateTime(2024, 6, 30) },
        // Plan 21 - Bluebird Federal Plus
        new BenefitDto { Id = 211, PlanId = 21, BenefitName = "Specialist Visit", CoverageType = "Medical", CopayAmount = 40.00m, StartDate = new DateTime(2022, 7, 1), EndDate = new DateTime(2024, 6, 30) },
        new BenefitDto { Id = 212, PlanId = 21, BenefitName = "Vision Comprehensive", CoverageType = "Vision", CopayAmount = 20.00m, StartDate = new DateTime(2022, 7, 1), EndDate = new DateTime(2024, 6, 30) },
        // Plan 30 - Unity Crest SA Standard
        new BenefitDto { Id = 301, PlanId = 30, BenefitName = "PCP Visit", CoverageType = "Medical", CopayAmount = 0.00m, StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) },
        new BenefitDto { Id = 302, PlanId = 30, BenefitName = "Dental Preventive", CoverageType = "Dental", CopayAmount = 0.00m, StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) },
        new BenefitDto { Id = 303, PlanId = 30, BenefitName = "Senior Rx", CoverageType = "Pharmacy", CopayAmount = 8.00m, StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) },
        // Plan 31 - Unity Crest SA Premium
        new BenefitDto { Id = 311, PlanId = 31, BenefitName = "Specialist Visit", CoverageType = "Medical", CopayAmount = 20.00m, StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) },
        new BenefitDto { Id = 312, PlanId = 31, BenefitName = "Behavioral Therapy", CoverageType = "Behavioral", CopayAmount = 15.00m, StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) },
        // Plan 40 - Moonstone Essential Care
        new BenefitDto { Id = 401, PlanId = 40, BenefitName = "Well Visit", CoverageType = "Medical", CopayAmount = 0.00m, StartDate = new DateTime(2023, 4, 1), EndDate = new DateTime(2025, 3, 31) },
        new BenefitDto { Id = 402, PlanId = 40, BenefitName = "Emergency Room", CoverageType = "Medical", CopayAmount = 50.00m, StartDate = new DateTime(2023, 4, 1), EndDate = new DateTime(2025, 3, 31) },
        new BenefitDto { Id = 403, PlanId = 40, BenefitName = "Generic Drugs", CoverageType = "Pharmacy", CopayAmount = 3.00m, StartDate = new DateTime(2023, 4, 1), EndDate = new DateTime(2025, 3, 31) },
        // Plan 41 - Moonstone Family Plan
        new BenefitDto { Id = 411, PlanId = 41, BenefitName = "Pediatric Visit", CoverageType = "Medical", CopayAmount = 0.00m, StartDate = new DateTime(2023, 4, 1), EndDate = new DateTime(2025, 3, 31) },
        new BenefitDto { Id = 412, PlanId = 41, BenefitName = "Vision Pediatric", CoverageType = "Vision", CopayAmount = 0.00m, StartDate = new DateTime(2023, 4, 1), EndDate = new DateTime(2025, 3, 31) },
        // Plan 42 - Moonstone Child Plan
        new BenefitDto { Id = 421, PlanId = 42, BenefitName = "Well Child Visit", CoverageType = "Medical", CopayAmount = 0.00m, StartDate = new DateTime(2023, 10, 1), EndDate = null },
        new BenefitDto { Id = 422, PlanId = 42, BenefitName = "Dental Child", CoverageType = "Dental", CopayAmount = 0.00m, StartDate = new DateTime(2023, 10, 1), EndDate = null },
        // Plan 50 - Citrine HMO Standard
        new BenefitDto { Id = 501, PlanId = 50, BenefitName = "Office Visit", CoverageType = "Medical", CopayAmount = 30.00m, StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2023, 12, 31) },
        new BenefitDto { Id = 502, PlanId = 50, BenefitName = "Lab Work", CoverageType = "Medical", CopayAmount = 15.00m, StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2023, 12, 31) },
        // Plan 51 - Citrine HMO Plus
        new BenefitDto { Id = 511, PlanId = 51, BenefitName = "Mental Health Visit", CoverageType = "Behavioral", CopayAmount = 25.00m, StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2023, 12, 31) },
        new BenefitDto { Id = 512, PlanId = 51, BenefitName = "Brand Rx", CoverageType = "Pharmacy", CopayAmount = 45.00m, StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2023, 12, 31) },
    };

    public ContractHierarchy(ILogger<ContractHierarchy> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ContractListResponse GetAllContracts()
    {
        _logger.LogInformation("GetAllContracts called — returning {Count} contracts", SampleContracts.Count);
        return new ContractListResponse { Contracts = SampleContracts };
    }

    /// <inheritdoc/>
    public PlanListResponse GetPlansByContractId(int contractId)
    {
        _logger.LogInformation("GetPlansByContractId called with contractId: {ContractId}", contractId);
        var plans = SamplePlans.Where(p => p.ContractId == contractId).ToList();
        return new PlanListResponse { Plans = plans };
    }

    /// <inheritdoc/>
    public BenefitListResponse GetBenefitsByPlanId(int planId)
    {
        _logger.LogInformation("GetBenefitsByPlanId called with planId: {PlanId}", planId);
        var benefits = SampleBenefits.Where(b => b.PlanId == planId).ToList();
        return new BenefitListResponse { Benefits = benefits };
    }

    /// <inheritdoc/>
    public SaveContractResponse SaveContract(ContractDto contract)
    {
        _logger.LogInformation("SaveContract called for: {Name}", contract.Name);

        // Validation
        if (string.IsNullOrWhiteSpace(contract.Name) || contract.Name.Length < 3)
            return new SaveContractResponse { Success = false, Message = "Contract Name is required and must be at least 3 characters." };

        if (contract.StartDate.HasValue && contract.EndDate.HasValue && contract.StartDate >= contract.EndDate)
            return new SaveContractResponse { Success = false, Message = "StartDate must be before EndDate." };

        if (contract.Id == 0)
        {
            // New contract
            contract.Id = Interlocked.Increment(ref _nextId);
            SampleContracts.Add(contract);
            _logger.LogInformation("New contract created with Id: {Id}", contract.Id);
        }
        else
        {
            // Update existing
            var existing = SampleContracts.FirstOrDefault(c => c.Id == contract.Id);
            if (existing == null)
                return new SaveContractResponse { Success = false, Message = $"Contract with Id {contract.Id} not found." };

            existing.Name = contract.Name;
            existing.ContractType = contract.ContractType;
            existing.StartDate = contract.StartDate;
            existing.EndDate = contract.EndDate;
            existing.IsActive = contract.IsActive;
        }

        return new SaveContractResponse { Success = true, Message = "Contract saved successfully.", Id = contract.Id };
    }

    /// <inheritdoc/>
    public SavePlanResponse SavePlan(PlanDto plan)
    {
        _logger.LogInformation("SavePlan called for: {PlanName}", plan.PlanName);

        // Validation
        if (string.IsNullOrWhiteSpace(plan.PlanName) || plan.PlanName.Length < 3)
            return new SavePlanResponse { Success = false, Message = "Plan Name is required and must be at least 3 characters." };

        if (plan.EffectiveDate.HasValue && plan.TermDate.HasValue && plan.EffectiveDate >= plan.TermDate)
            return new SavePlanResponse { Success = false, Message = "EffectiveDate must be before TermDate." };

        if (plan.Id == 0)
        {
            // New plan
            plan.Id = Interlocked.Increment(ref _nextId);
            SamplePlans.Add(plan);

            // Update parent contract's PlanCount
            var contract = SampleContracts.FirstOrDefault(c => c.Id == plan.ContractId);
            if (contract != null)
                contract.PlanCount = SamplePlans.Count(p => p.ContractId == plan.ContractId);

            _logger.LogInformation("New plan created with Id: {Id}", plan.Id);
        }
        else
        {
            // Update existing
            var existing = SamplePlans.FirstOrDefault(p => p.Id == plan.Id);
            if (existing == null)
                return new SavePlanResponse { Success = false, Message = $"Plan with Id {plan.Id} not found." };

            existing.PlanName = plan.PlanName;
            existing.PlanCode = plan.PlanCode;
            existing.EffectiveDate = plan.EffectiveDate;
            existing.TermDate = plan.TermDate;
            existing.Status = plan.Status;
        }

        return new SavePlanResponse { Success = true, Message = "Plan saved successfully.", Id = plan.Id };
    }

    /// <inheritdoc/>
    public SaveBenefitResponse SaveBenefit(BenefitDto benefit)
    {
        _logger.LogInformation("SaveBenefit called for: {BenefitName}", benefit.BenefitName);

        // Validation
        if (string.IsNullOrWhiteSpace(benefit.BenefitName) || benefit.BenefitName.Length < 3)
            return new SaveBenefitResponse { Success = false, Message = "Benefit Name is required and must be at least 3 characters." };

        if (benefit.StartDate.HasValue && benefit.EndDate.HasValue && benefit.StartDate >= benefit.EndDate)
            return new SaveBenefitResponse { Success = false, Message = "StartDate must be before EndDate." };

        if (benefit.CopayAmount < 0)
            return new SaveBenefitResponse { Success = false, Message = "CopayAmount must be >= 0." };

        if (benefit.Id == 0)
        {
            // New benefit
            benefit.Id = Interlocked.Increment(ref _nextId);
            SampleBenefits.Add(benefit);

            // Update parent plan's BenefitCount
            var plan = SamplePlans.FirstOrDefault(p => p.Id == benefit.PlanId);
            if (plan != null)
                plan.BenefitCount = SampleBenefits.Count(b => b.PlanId == benefit.PlanId);

            _logger.LogInformation("New benefit created with Id: {Id}", benefit.Id);
        }
        else
        {
            // Update existing
            var existing = SampleBenefits.FirstOrDefault(b => b.Id == benefit.Id);
            if (existing == null)
                return new SaveBenefitResponse { Success = false, Message = $"Benefit with Id {benefit.Id} not found." };

            existing.BenefitName = benefit.BenefitName;
            existing.CoverageType = benefit.CoverageType;
            existing.CopayAmount = benefit.CopayAmount;
            existing.StartDate = benefit.StartDate;
            existing.EndDate = benefit.EndDate;
        }

        return new SaveBenefitResponse { Success = true, Message = "Benefit saved successfully.", Id = benefit.Id };
    }

    /// <inheritdoc/>
    public BatchSaveResponse BatchSaveContracts(BatchSaveContractsRequest request)
    {
        _logger.LogInformation("BatchSaveContracts called with {Count} items", request.Contracts.Count);

        var errors = new List<string>();
        var savedCount = 0;

        foreach (var contract in request.Contracts)
        {
            var result = SaveContract(contract);
            if (result.Success)
                savedCount++;
            else
                errors.Add($"Contract '{contract.Name}': {result.Message}");
        }

        return new BatchSaveResponse
        {
            Success = errors.Count == 0,
            SavedCount = savedCount,
            Message = errors.Count == 0
                ? $"Successfully saved {savedCount} contract(s)."
                : $"Saved {savedCount} contract(s) with {errors.Count} error(s).",
            Errors = errors
        };
    }

    /// <inheritdoc/>
    public BatchSaveResponse BatchSavePlans(BatchSavePlansRequest request)
    {
        _logger.LogInformation("BatchSavePlans called with {Count} items", request.Plans.Count);

        var errors = new List<string>();
        var savedCount = 0;

        foreach (var plan in request.Plans)
        {
            var result = SavePlan(plan);
            if (result.Success)
                savedCount++;
            else
                errors.Add($"Plan '{plan.PlanName}': {result.Message}");
        }

        return new BatchSaveResponse
        {
            Success = errors.Count == 0,
            SavedCount = savedCount,
            Message = errors.Count == 0
                ? $"Successfully saved {savedCount} plan(s)."
                : $"Saved {savedCount} plan(s) with {errors.Count} error(s).",
            Errors = errors
        };
    }

    /// <inheritdoc/>
    public BatchSaveResponse BatchSaveBenefits(BatchSaveBenefitsRequest request)
    {
        _logger.LogInformation("BatchSaveBenefits called with {Count} items", request.Benefits.Count);

        var errors = new List<string>();
        var savedCount = 0;

        foreach (var benefit in request.Benefits)
        {
            var result = SaveBenefit(benefit);
            if (result.Success)
                savedCount++;
            else
                errors.Add($"Benefit '{benefit.BenefitName}': {result.Message}");
        }

        return new BatchSaveResponse
        {
            Success = errors.Count == 0,
            SavedCount = savedCount,
            Message = errors.Count == 0
                ? $"Successfully saved {savedCount} benefit(s)."
                : $"Saved {savedCount} benefit(s) with {errors.Count} error(s).",
            Errors = errors
        };
    }
}

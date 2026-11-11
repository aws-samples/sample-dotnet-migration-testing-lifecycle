using System.ServiceModel;
using PayorContracts.Web.Models;

namespace PayorContracts.Web.Services;

/// <summary>
/// Service class that calls CoreWCF ContractHierarchy via WCF ChannelFactory.
/// Injected directly into Blazor components via DI.
/// </summary>
public class ContractHierarchyService
{
    private readonly string _endpointUrl;
    private readonly ILogger<ContractHierarchyService> _logger;

    public ContractHierarchyService(IConfiguration configuration, ILogger<ContractHierarchyService> logger)
    {
        _endpointUrl = configuration["WcfService:ContractHierarchyEndpointUrl"]
            ?? throw new InvalidOperationException("WcfService:ContractHierarchyEndpointUrl is not configured.");
        _logger = logger;
    }

    private IContractHierarchy CreateChannel()
    {
        var binding = new BasicHttpBinding(BasicHttpSecurityMode.None)
        {
            MaxReceivedMessageSize = int.MaxValue
        };
        var endpoint = new EndpointAddress(_endpointUrl);
        var factory = new ChannelFactory<IContractHierarchy>(binding, endpoint);
        return factory.CreateChannel();
    }

    /// <summary>
    /// Gets all contracts from the CoreWCF service.
    /// </summary>
    public List<ContractDto> GetAllContracts()
    {
        _logger.LogInformation("Calling CoreWCF GetAllContracts");
        var channel = CreateChannel();

        try
        {
            var response = channel.GetAllContracts();
            return response.Contracts ?? new List<ContractDto>();
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    /// <summary>
    /// Gets plans for a specific contract from the CoreWCF service.
    /// </summary>
    public List<PlanDto> GetPlansByContractId(int contractId)
    {
        _logger.LogInformation("Calling CoreWCF GetPlansByContractId for ContractId: {ContractId}", contractId);
        var channel = CreateChannel();

        try
        {
            var response = channel.GetPlansByContractId(contractId);
            return response.Plans ?? new List<PlanDto>();
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    /// <summary>
    /// Gets benefits for a specific plan from the CoreWCF service.
    /// </summary>
    public List<BenefitDto> GetBenefitsByPlanId(int planId)
    {
        _logger.LogInformation("Calling CoreWCF GetBenefitsByPlanId for PlanId: {PlanId}", planId);
        var channel = CreateChannel();

        try
        {
            var response = channel.GetBenefitsByPlanId(planId);
            return response.Benefits ?? new List<BenefitDto>();
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    /// <summary>
    /// Saves a single contract via the CoreWCF service.
    /// </summary>
    public SaveContractResponse SaveContract(ContractDto contract)
    {
        _logger.LogInformation("Calling CoreWCF SaveContract for: {Name}", contract.Name);
        var channel = CreateChannel();

        try
        {
            return channel.SaveContract(contract);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    /// <summary>
    /// Saves a single plan via the CoreWCF service.
    /// </summary>
    public SavePlanResponse SavePlan(PlanDto plan)
    {
        _logger.LogInformation("Calling CoreWCF SavePlan for: {PlanName}", plan.PlanName);
        var channel = CreateChannel();

        try
        {
            return channel.SavePlan(plan);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    /// <summary>
    /// Saves a single benefit via the CoreWCF service.
    /// </summary>
    public SaveBenefitResponse SaveBenefit(BenefitDto benefit)
    {
        _logger.LogInformation("Calling CoreWCF SaveBenefit for: {BenefitName}", benefit.BenefitName);
        var channel = CreateChannel();

        try
        {
            return channel.SaveBenefit(benefit);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    /// <summary>
    /// Batch saves multiple contracts via the CoreWCF service.
    /// </summary>
    public BatchSaveResponse BatchSaveContracts(List<ContractDto> contracts)
    {
        _logger.LogInformation("Calling CoreWCF BatchSaveContracts with {Count} items", contracts.Count);
        var channel = CreateChannel();

        try
        {
            var request = new BatchSaveContractsRequest { Contracts = contracts };
            return channel.BatchSaveContracts(request);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    /// <summary>
    /// Batch saves multiple plans via the CoreWCF service.
    /// </summary>
    public BatchSaveResponse BatchSavePlans(List<PlanDto> plans)
    {
        _logger.LogInformation("Calling CoreWCF BatchSavePlans with {Count} items", plans.Count);
        var channel = CreateChannel();

        try
        {
            var request = new BatchSavePlansRequest { Plans = plans };
            return channel.BatchSavePlans(request);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    /// <summary>
    /// Batch saves multiple benefits via the CoreWCF service.
    /// </summary>
    public BatchSaveResponse BatchSaveBenefits(List<BenefitDto> benefits)
    {
        _logger.LogInformation("Calling CoreWCF BatchSaveBenefits with {Count} items", benefits.Count);
        var channel = CreateChannel();

        try
        {
            var request = new BatchSaveBenefitsRequest { Benefits = benefits };
            return channel.BatchSaveBenefits(request);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }
}

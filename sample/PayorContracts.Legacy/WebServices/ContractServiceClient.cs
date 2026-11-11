using System.ServiceModel;
using PayorContracts.Legacy.Models;
using PayorContracts.Legacy.ServiceProxy;

namespace PayorContracts.Legacy.WebServices;

/// <summary>
/// WCF client that calls the CoreWCF ContractHierarchy service.
/// Uses ChannelFactory with BasicHttpBinding.
/// </summary>
public class ContractServiceClient
{
    private readonly string _endpointUrl = "http://localhost:5000/ContractHierarchy.svc";

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

    public ContractListResponse GetAllContracts()
    {
        var channel = CreateChannel();
        try
        {
            return channel.GetAllContracts();
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    public PlanListResponse GetPlansByContractId(int contractId)
    {
        var channel = CreateChannel();
        try
        {
            return channel.GetPlansByContractId(contractId);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    public BenefitListResponse GetBenefitsByPlanId(int planId)
    {
        var channel = CreateChannel();
        try
        {
            return channel.GetBenefitsByPlanId(planId);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    public SaveContractResponse SaveContract(ContractDto contract)
    {
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

    public SavePlanResponse SavePlan(PlanDto plan)
    {
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

    public SaveBenefitResponse SaveBenefit(BenefitDto benefit)
    {
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

    public BatchSaveResponse BatchSaveContracts(BatchSaveContractsRequest request)
    {
        var channel = CreateChannel();
        try
        {
            return channel.BatchSaveContracts(request);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    public BatchSaveResponse BatchSavePlans(BatchSavePlansRequest request)
    {
        var channel = CreateChannel();
        try
        {
            return channel.BatchSavePlans(request);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    public BatchSaveResponse BatchSaveBenefits(BatchSaveBenefitsRequest request)
    {
        var channel = CreateChannel();
        try
        {
            return channel.BatchSaveBenefits(request);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }
}

using System.ServiceModel;
using PayorContracts.Legacy.Models;

namespace PayorContracts.Legacy.ServiceProxy;

/// <summary>
/// WCF service contract interface matching the CoreWCF server's IContractHierarchy.
/// Used by ChannelFactory to create a typed client proxy.
/// </summary>
[ServiceContract(Namespace = "http://tempuri.org/")]
public interface IContractHierarchy
{
    [OperationContract]
    ContractListResponse GetAllContracts();

    [OperationContract]
    PlanListResponse GetPlansByContractId(int contractId);

    [OperationContract]
    BenefitListResponse GetBenefitsByPlanId(int planId);

    [OperationContract]
    SaveContractResponse SaveContract(ContractDto contract);

    [OperationContract]
    SavePlanResponse SavePlan(PlanDto plan);

    [OperationContract]
    SaveBenefitResponse SaveBenefit(BenefitDto benefit);

    [OperationContract]
    BatchSaveResponse BatchSaveContracts(BatchSaveContractsRequest request);

    [OperationContract]
    BatchSaveResponse BatchSavePlans(BatchSavePlansRequest request);

    [OperationContract]
    BatchSaveResponse BatchSaveBenefits(BatchSaveBenefitsRequest request);
}

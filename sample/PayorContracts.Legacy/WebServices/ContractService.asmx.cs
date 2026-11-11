using Microsoft.AspNetCore.Mvc;
using PayorContracts.Legacy.Models;

namespace PayorContracts.Legacy.WebServices;

/// <summary>
/// Simulates the legacy ASMX web service (ContractService.asmx).
/// Keeps the same URL pattern: /WebServices/ContractService.asmx/GetContracts
/// Called via jQuery AJAX from the Contract Hierarchy page.
/// Internally calls CoreWCF service via WCF ChannelFactory.
/// </summary>
[Route("WebServices/ContractService.asmx")]
[ApiController]
public class ContractServiceController : ControllerBase
{
    private readonly ContractServiceClient _wcfClient;

    public ContractServiceController(ContractServiceClient wcfClient)
    {
        _wcfClient = wcfClient;
    }

    /// <summary>
    /// Gets all contracts.
    /// </summary>
    [HttpPost("GetContracts")]
    public ActionResult GetContracts()
    {
        var result = _wcfClient.GetAllContracts();
        return Ok(result);
    }

    /// <summary>
    /// Gets plans for a specific contract.
    /// </summary>
    [HttpPost("GetPlansByContractId")]
    public ActionResult GetPlansByContractId([FromBody] ContractIdRequest request)
    {
        var result = _wcfClient.GetPlansByContractId(request.ContractId);
        return Ok(result);
    }

    /// <summary>
    /// Gets benefits for a specific plan.
    /// </summary>
    [HttpPost("GetBenefitsByPlanId")]
    public ActionResult GetBenefitsByPlanId([FromBody] PlanIdRequest request)
    {
        var result = _wcfClient.GetBenefitsByPlanId(request.PlanId);
        return Ok(result);
    }

    /// <summary>
    /// Saves a single contract.
    /// </summary>
    [HttpPost("SaveContract")]
    public ActionResult SaveContract([FromBody] ContractDto contract)
    {
        var result = _wcfClient.SaveContract(contract);
        return Ok(result);
    }

    /// <summary>
    /// Saves a single plan.
    /// </summary>
    [HttpPost("SavePlan")]
    public ActionResult SavePlan([FromBody] PlanDto plan)
    {
        var result = _wcfClient.SavePlan(plan);
        return Ok(result);
    }

    /// <summary>
    /// Saves a single benefit.
    /// </summary>
    [HttpPost("SaveBenefit")]
    public ActionResult SaveBenefit([FromBody] BenefitDto benefit)
    {
        var result = _wcfClient.SaveBenefit(benefit);
        return Ok(result);
    }

    /// <summary>
    /// Batch saves multiple contracts.
    /// </summary>
    [HttpPost("BatchSaveContracts")]
    public ActionResult BatchSaveContracts([FromBody] BatchSaveContractsRequest request)
    {
        var result = _wcfClient.BatchSaveContracts(request);
        return Ok(result);
    }

    /// <summary>
    /// Batch saves multiple plans.
    /// </summary>
    [HttpPost("BatchSavePlans")]
    public ActionResult BatchSavePlans([FromBody] BatchSavePlansRequest request)
    {
        var result = _wcfClient.BatchSavePlans(request);
        return Ok(result);
    }

    /// <summary>
    /// Batch saves multiple benefits.
    /// </summary>
    [HttpPost("BatchSaveBenefits")]
    public ActionResult BatchSaveBenefits([FromBody] BatchSaveBenefitsRequest request)
    {
        var result = _wcfClient.BatchSaveBenefits(request);
        return Ok(result);
    }
}

// --- Request models for the API ---

public class ContractIdRequest
{
    public int ContractId { get; set; }
}

public class PlanIdRequest
{
    public int PlanId { get; set; }
}

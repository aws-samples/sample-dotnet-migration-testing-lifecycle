using System.Runtime.Serialization;

namespace PayorContracts.Legacy.Models;

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class ContractDto
{
    [DataMember] public int Id { get; set; }
    [DataMember] public string Name { get; set; } = string.Empty;
    [DataMember] public string ContractType { get; set; } = string.Empty;
    [DataMember] public DateTime? StartDate { get; set; }
    [DataMember] public DateTime? EndDate { get; set; }
    [DataMember] public bool IsActive { get; set; }
    [DataMember] public int PlanCount { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class PlanDto
{
    [DataMember] public int Id { get; set; }
    [DataMember] public int ContractId { get; set; }
    [DataMember] public string PlanName { get; set; } = string.Empty;
    [DataMember] public string PlanCode { get; set; } = string.Empty;
    [DataMember] public DateTime? EffectiveDate { get; set; }
    [DataMember] public DateTime? TermDate { get; set; }
    [DataMember] public string Status { get; set; } = string.Empty;
    [DataMember] public int BenefitCount { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BenefitDto
{
    [DataMember] public int Id { get; set; }
    [DataMember] public int PlanId { get; set; }
    [DataMember] public string BenefitName { get; set; } = string.Empty;
    [DataMember] public string CoverageType { get; set; } = string.Empty;
    [DataMember] public decimal CopayAmount { get; set; }
    [DataMember] public DateTime? StartDate { get; set; }
    [DataMember] public DateTime? EndDate { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class ContractListResponse
{
    [DataMember] public List<ContractDto> Contracts { get; set; } = new();
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class PlanListResponse
{
    [DataMember] public List<PlanDto> Plans { get; set; } = new();
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BenefitListResponse
{
    [DataMember] public List<BenefitDto> Benefits { get; set; } = new();
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class SaveContractResponse
{
    [DataMember] public bool Success { get; set; }
    [DataMember] public string Message { get; set; } = string.Empty;
    [DataMember] public int Id { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class SavePlanResponse
{
    [DataMember] public bool Success { get; set; }
    [DataMember] public string Message { get; set; } = string.Empty;
    [DataMember] public int Id { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class SaveBenefitResponse
{
    [DataMember] public bool Success { get; set; }
    [DataMember] public string Message { get; set; } = string.Empty;
    [DataMember] public int Id { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BatchSaveContractsRequest
{
    [DataMember] public List<ContractDto> Contracts { get; set; } = new();
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BatchSavePlansRequest
{
    [DataMember] public List<PlanDto> Plans { get; set; } = new();
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BatchSaveBenefitsRequest
{
    [DataMember] public List<BenefitDto> Benefits { get; set; } = new();
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BatchSaveResponse
{
    [DataMember] public bool Success { get; set; }
    [DataMember] public int SavedCount { get; set; }
    [DataMember] public string Message { get; set; } = string.Empty;
    [DataMember] public List<string> Errors { get; set; } = new();
}

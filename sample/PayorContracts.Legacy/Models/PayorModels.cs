using System.Runtime.Serialization;

namespace PayorContracts.Legacy.Models;

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class PayorDto
{
    [DataMember] public int Id { get; set; }
    [DataMember] public string Name { get; set; } = string.Empty;
    [DataMember] public string Code { get; set; } = string.Empty;
    [DataMember] public bool IsActive { get; set; }
    [DataMember] public string Category { get; set; } = string.Empty;
    [DataMember] public DateTime? EffectiveDate { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class PayorListResponse
{
    [DataMember] public List<PayorDto> Payors { get; set; } = new();
    [DataMember] public int Count { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class PayorDetailResponse
{
    [DataMember] public int Id { get; set; }
    [DataMember] public string Name { get; set; } = string.Empty;
    [DataMember] public string Code { get; set; } = string.Empty;
    [DataMember] public bool IsActive { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class UpdatePayorRequest
{
    [DataMember] public int Id { get; set; }
    [DataMember] public string Name { get; set; } = string.Empty;
    [DataMember] public string Code { get; set; } = string.Empty;
    [DataMember] public bool IsActive { get; set; }
    [DataMember] public string Category { get; set; } = string.Empty;
    [DataMember] public DateTime? EffectiveDate { get; set; }
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class UpdatePayorResponse
{
    [DataMember] public bool Success { get; set; }
    [DataMember] public string Message { get; set; } = string.Empty;
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BatchUpdatePayorRequest
{
    [DataMember] public List<UpdatePayorRequest> Payors { get; set; } = new();
}

[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BatchUpdatePayorResponse
{
    [DataMember] public bool Success { get; set; }
    [DataMember] public int UpdatedCount { get; set; }
    [DataMember] public string Message { get; set; } = string.Empty;
    [DataMember] public List<string> Errors { get; set; } = new();
}

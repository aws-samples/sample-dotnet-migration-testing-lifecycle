using System.Runtime.Serialization;
using System.ServiceModel;

namespace PayorContracts.Web.Models;

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

[ServiceContract(Namespace = "http://tempuri.org/")]
public interface IPayorInformation
{
    [OperationContract]
    PayorListResponse GetAllPayors();

    [OperationContract]
    PayorDetailResponse GetPayorById(int id);

    [OperationContract]
    UpdatePayorResponse UpdatePayor(UpdatePayorRequest request);

    [OperationContract]
    BatchUpdatePayorResponse BatchUpdatePayors(BatchUpdatePayorRequest request);
}


/// <summary>
/// Editable payor model for the PayorEdit page with batch editing.
/// Includes additional fields (Category, EffectiveDate, MaxContractValue)
/// to demonstrate different edit control types.
/// </summary>
public class EditablePayorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime? EffectiveDate { get; set; }
    public decimal MaxContractValue { get; set; }

    /// <summary>
    /// When true, the row is included in the batch save.
    /// When false (unchecked), the row is marked for deletion.
    /// </summary>
    public bool IsIncluded { get; set; } = true;
}

using System.Runtime.Serialization;
using CoreWCF;

namespace PayorContracts.Host.Contracts;

/// <summary>
/// Service contract for payor information operations.
/// Shared between PayorContracts.Host (server) and client consumers via the shared service-interface NuGet package.
/// </summary>
[ServiceContract]
public interface IPayorInformation
{
    /// <summary>Retrieves all payors in the system.</summary>
    [OperationContract]
    PayorListResponse GetAllPayors();

    /// <summary>Retrieves a single payor by its identifier.</summary>
    /// <param name="id">The payor identifier.</param>
    [OperationContract]
    PayorDetailResponse GetPayorById(int id);

    /// <summary>Updates an existing payor record.</summary>
    [OperationContract]
    UpdatePayorResponse UpdatePayor(UpdatePayorRequest request);

    /// <summary>Batch updates multiple payor records.</summary>
    [OperationContract]
    BatchUpdatePayorResponse BatchUpdatePayors(BatchUpdatePayorRequest request);
}

/// <summary>Response containing a list of all payors.</summary>
[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class PayorListResponse
{
    [DataMember]
    public List<PayorDto> Payors { get; set; } = new();

    [DataMember]
    public int Count { get; set; }
}

/// <summary>Response containing detailed information for a single payor.</summary>
[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class PayorDetailResponse
{
    [DataMember]
    public int Id { get; set; }

    [DataMember]
    public string Name { get; set; } = string.Empty;

    [DataMember]
    public string Code { get; set; } = string.Empty;

    [DataMember]
    public bool IsActive { get; set; }
}

/// <summary>Data transfer object representing a payor record.</summary>
[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class PayorDto
{
    [DataMember]
    public int Id { get; set; }

    [DataMember]
    public string Name { get; set; } = string.Empty;

    [DataMember]
    public string Code { get; set; } = string.Empty;

    [DataMember]
    public bool IsActive { get; set; }

    /// <summary>Payor category (e.g., Commercial, Medicare, Medicaid, Tricare).</summary>
    [DataMember]
    public string Category { get; set; } = string.Empty;

    /// <summary>Contract effective date.</summary>
    [DataMember]
    public DateTime? EffectiveDate { get; set; }
}

/// <summary>Request to update a single payor record.</summary>
[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class UpdatePayorRequest
{
    [DataMember]
    public int Id { get; set; }

    [DataMember]
    public string Name { get; set; } = string.Empty;

    [DataMember]
    public string Code { get; set; } = string.Empty;

    [DataMember]
    public bool IsActive { get; set; }

    [DataMember]
    public string Category { get; set; } = string.Empty;

    [DataMember]
    public DateTime? EffectiveDate { get; set; }
}

/// <summary>Response from a single update operation.</summary>
[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class UpdatePayorResponse
{
    [DataMember]
    public bool Success { get; set; }

    [DataMember]
    public string Message { get; set; } = string.Empty;
}

/// <summary>Request to batch update multiple payor records.</summary>
[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BatchUpdatePayorRequest
{
    [DataMember]
    public List<UpdatePayorRequest> Payors { get; set; } = new();
}

/// <summary>Response from a batch update operation.</summary>
[DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PayorContracts.Host.Contracts")]
public class BatchUpdatePayorResponse
{
    [DataMember]
    public bool Success { get; set; }

    [DataMember]
    public int UpdatedCount { get; set; }

    [DataMember]
    public string Message { get; set; } = string.Empty;

    [DataMember]
    public List<string> Errors { get; set; } = new();
}

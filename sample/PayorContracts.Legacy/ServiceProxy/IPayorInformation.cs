using System.ServiceModel;
using PayorContracts.Legacy.Models;

namespace PayorContracts.Legacy.ServiceProxy;

/// <summary>
/// WCF service contract interface matching the CoreWCF server's IPayorInformation.
/// Used by ChannelFactory to create a typed client proxy.
/// </summary>
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

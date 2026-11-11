using System.ServiceModel;
using PayorContracts.Legacy.Models;
using PayorContracts.Legacy.ServiceProxy;

namespace PayorContracts.Legacy.WebServices;

/// <summary>
/// WCF client that calls the CoreWCF PayorInformation service.
/// Uses ChannelFactory with BasicHttpBinding.
/// </summary>
public class PayorServiceClient
{
    private readonly string _endpointUrl = "http://localhost:5000/PayorInformation.svc";

    private IPayorInformation CreateChannel()
    {
        var binding = new BasicHttpBinding(BasicHttpSecurityMode.None)
        {
            MaxReceivedMessageSize = int.MaxValue
        };
        var endpoint = new EndpointAddress(_endpointUrl);
        var factory = new ChannelFactory<IPayorInformation>(binding, endpoint);
        return factory.CreateChannel();
    }

    public PayorListResponse GetAllPayors()
    {
        var channel = CreateChannel();
        try
        {
            return channel.GetAllPayors();
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    public PayorDetailResponse GetPayorById(int id)
    {
        var channel = CreateChannel();
        try
        {
            return channel.GetPayorById(id);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    public UpdatePayorResponse UpdatePayor(UpdatePayorRequest request)
    {
        var channel = CreateChannel();
        try
        {
            return channel.UpdatePayor(request);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }

    public BatchUpdatePayorResponse BatchUpdatePayors(BatchUpdatePayorRequest request)
    {
        var channel = CreateChannel();
        try
        {
            return channel.BatchUpdatePayors(request);
        }
        finally
        {
            ((IClientChannel)channel).Close();
        }
    }
}

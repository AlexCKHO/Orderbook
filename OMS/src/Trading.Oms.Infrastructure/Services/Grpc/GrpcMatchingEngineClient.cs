using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Infrastructure.Services.Grpc;

public class GrpcMatchingEngineClient : IMatchingEngineClient
{
    public Task<EnginePlaceOrderResult> PlaceOrderCommand(PlaceOrderCommand cmd, ulong orderId)
    {
    
    }

    public Task<EngineCancelOrderResult> CancelOrderCommand(CancelOrderCommand cmd)
    {
        throw new NotImplementedException();
    }
}
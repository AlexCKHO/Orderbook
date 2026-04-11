using Orderbook;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Infrastructure.Grpc;

public class GrpcMatchingEngineClient(MatchingEngine.MatchingEngineClient client) : IMatchingEngineClient
{
    public Task<EnginePlaceOrderResult> PlaceOrderCommand(PlaceOrderCommand cmd, ulong orderId)
    { 
        using var call = client.PlaceBatchStream();
        
    }

    public Task<EngineCancelOrderResult> CancelOrderCommand(CancelOrderCommand cmd)
    {
        throw new NotImplementedException();
    }
}
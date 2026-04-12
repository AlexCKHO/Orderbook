using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Infrastructure.Services.Mock;

public class MockMatchingEngineClient : IMatchingEngineClient
{
    public async Task<EnginePlaceOrderResult> PlaceOrderCommand(PlaceOrderCommand cmd, ulong orderId)
    {
        // await engine grpc pass cmd
        // return new EnginePlaceOrderResult(Status.Submitted, orderId);

        throw new NotImplementedException();
    }

    public async Task<EngineCancelOrderResult> CancelOrderCommand(CancelOrderCommand cmd)
    {
        // await engine grpc pass cmd
        // return new EngineCancelOrderResult(Status.Submitted, cmd.ClientOrderId);
        throw new NotImplementedException();
    }
}
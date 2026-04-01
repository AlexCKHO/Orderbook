using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Application.Interfaces;

public interface IMatchingEngineClient
{
    public Task<EnginePlaceOrderResult> PlaceOrderCommand(PlaceOrderCommand cmd, ulong orderId);
    public Task<EngineCancelOrderResult> CancelOrderCommand(CancelOrderCommand cmd);
}
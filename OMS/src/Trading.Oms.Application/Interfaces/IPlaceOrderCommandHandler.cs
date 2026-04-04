using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Application.Interfaces;

public interface IPlaceOrderCommandHandler
{
    public Task<CommandAckResult?> HandleAsync(PlaceOrderCommand cmd, CancellationToken token);
}
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Application.Interfaces;

public interface ICancelOrderCommandHandler
{
    public Task<CommandAckResult> HandleAsync(CancelOrderCommand cmd, CancellationToken token);
}
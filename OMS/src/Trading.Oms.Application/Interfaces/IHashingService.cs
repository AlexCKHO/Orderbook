using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Interfaces;

public interface IHashingService
{
    public string HashPlaceOrderCommand(uint accountId, string symbol, Side side, OrderType orderType,
        ulong quantity,
        ulong? price);

    public string HashCancelOrderCommand(uint accountId, ulong orderId);
}
using Trading.Oms.Application.Commands;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Api.Contracts;


public sealed record PlaceOrderRequest(
    uint AccountId,
    string Symbol,
    Side Side,
    OrderType OrderType,
    ulong Quantity,
    ulong? Price
);
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Infrastructure.Services;

public class Sha256HashingService : IHashingService
{
    public string HashPlaceOrderCommand(uint accountId, string symbol, Side side, OrderType orderType,
        ulong quantity,
        ulong? price)
    {
        var canonicalObject = new
        {
            AccountId = accountId,
            Symbol = symbol,
            Side = side,
            OrderType = orderType,
            Quantity = quantity,
            Price = price
        };

        string payloadString = JsonSerializer.Serialize(canonicalObject);

        string hashedRequest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadString)));

        return hashedRequest;
    }

    public string HashCancelOrderCommand(uint accountId, ulong orderId)
    {
        var canonicalObject = new
        {
            AccountId = accountId,
            OrderId = orderId
        };

        string payloadString = JsonSerializer.Serialize(canonicalObject);

        string hashedRequest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadString)));

        return hashedRequest;
    }
}
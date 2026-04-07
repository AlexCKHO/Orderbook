using Trading.Oms.Domain.Enums;
using Trading.Oms.Infrastructure.Services;

namespace Trading.Oms.Infrastructure.Tests.Services;

public class Sha256HashingServiceTests
{
    readonly Sha256HashingService _service = new();

    [Test]
    public void HashPlaceOrderCommand_ReturnsHashOfCanonicalPayload()
    {
        var hash = _service.HashPlaceOrderCommand(
            accountId: 123,
            symbol: "BTC-USD",
            side: Side.Bid,
            orderType: OrderType.Limit,
            quantity: 10,
            price: 50000);

        Assert.That(hash, Is.EqualTo("22FE290B946440CEC858E0BDB242D5209373066358FF8B74FC6C5CD0638C10A8"));
    }

    [Test]
    public void HashPlaceOrderCommand_IncludesNullPriceInCanonicalPayload()
    {
        var hash = _service.HashPlaceOrderCommand(
            accountId: 123,
            symbol: "ETH-USD",
            side: Side.Ask,
            orderType: OrderType.Market,
            quantity: 25,
            price: null);

        Assert.That(hash, Is.EqualTo("CFFA8D194CEF4AE9EB60922B7C213A26CD204148921B2DFA868CB8B43936AC3B"));
    }

    [Test]
    public void HashPlaceOrderCommand_ChangesWhenPayloadChanges()
    {
        var firstHash = _service.HashPlaceOrderCommand(1, "BTC-USD", Side.Bid, OrderType.Limit, 10, 50000);
        var secondHash = _service.HashPlaceOrderCommand(2, "BTC-USD", Side.Bid, OrderType.Limit, 10, 50000);

        Assert.That(firstHash, Is.EqualTo("EE288B1D8E059F4C7A8A12D009F96BD8CAC5DB4963E1B16D6B5ABA21F15601E9"));
        Assert.That(secondHash, Is.Not.EqualTo(firstHash));
    }

    [Test]
    public void HashCancelOrderCommand_ReturnsHashOfCanonicalPayload()
    {
        var hash = _service.HashCancelOrderCommand(accountId: 123, orderId: 987654321);

        Assert.That(hash, Is.EqualTo("CAA664181A4BB62365073EF5AE83B8E6328ED5F2B894447ECB1ED9C0791DD9A5"));
    }
}

using Trading.Oms.Domain.Enums;
using Trading.Oms.Infrastructure.Services.Mock;
using Trading.Oms.Infrastructure.Tests.Infrastructure;

namespace Trading.Oms.Infrastructure.Tests.Services;

public class MockMatchingEngineClientTests
{
    readonly MockMatchingEngineClient _client = new();

    [Test]
    public async Task PlaceOrderCommand_ReturnsSubmittedResultWithProvidedOrderId()
    {
        var command = TestData.PlaceOrderCommand();

        var result = await _client.PlaceOrderCommand(command, orderId: 42);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(Status.Submitted));
            Assert.That(result.OrderId, Is.EqualTo(42));
            Assert.That(result.RejectionCode, Is.Null);
            Assert.That(result.RejectionReason, Is.Null);
        });
    }

    [Test]
    public async Task CancelOrderCommand_ReturnsSubmittedResultWithCommandOrderId()
    {
        var command = TestData.CancelOrderCommand(orderId: 987654321);

        var result = await _client.CancelOrderCommand(command);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(Status.Submitted));
            Assert.That(result.OrderId, Is.EqualTo(command.ClientOrderId));
            Assert.That(result.RejectionCode, Is.Null);
            Assert.That(result.RejectionReason, Is.Null);
        });
    }
}

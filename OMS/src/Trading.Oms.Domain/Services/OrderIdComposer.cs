using Trading.Oms.Api.Oms.Domain.Interface;

namespace Trading.Oms.Api.Oms.Domain.Services;

public class OrderIdComposer : IOrderIdComposer
{
    public ulong Compose(uint accountId, uint sequence)
    {
        return ((ulong)accountId << 32) | sequence;
    }

    public (uint accountId, uint sequence) Decompose(uint orderId)
    {
        uint accountId = orderId >> 32;
        uint sequence = orderId;

        return (accountId, sequence);
    }
}
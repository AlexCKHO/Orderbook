using Trading.Oms.Application.Interfaces;

namespace Trading.Oms.Infrastructure.Services.Mock;

public class MockOrderSequenceAllocator : IOrderSequenceAllocator
{
    private Dictionary<uint, uint> _orderIds = new Dictionary<uint, uint>();

    public Task<uint> AllocateNextSequenceForAccount(uint accountId)
    {
        if (!_orderIds.ContainsKey(accountId))
        {
            _orderIds[accountId] = 0;
            return Task.FromResult((uint)0);
        }
        else
        {
            var sequence = ++_orderIds[accountId];
            return Task.FromResult(sequence);
        }
    }
}
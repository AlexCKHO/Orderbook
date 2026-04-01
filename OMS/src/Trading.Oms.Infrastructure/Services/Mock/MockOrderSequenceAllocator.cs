using Trading.Oms.Application.Interfaces;
using System.Collections.Concurrent;

namespace Trading.Oms.Infrastructure.Services.Mock;

public class MockOrderSequenceAllocator : IOrderSequenceAllocator
{
    private ConcurrentDictionary<uint, uint> _orderIds = new();

    public Task<uint> AllocateNextSequenceForAccount(uint accountId)
    {
        var nextSequence = _orderIds.AddOrUpdate(
            accountId,
            1,
            (key, oldValue) => oldValue + 1
        );

        return Task.FromResult(nextSequence);
    }
}
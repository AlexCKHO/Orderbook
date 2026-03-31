using Trading.Oms.Application.Interfaces;

namespace Trading.Oms.Infrastructure.Services;

public class MockOrderSequenceAllocator : IOrderSequenceAllocator
{
    private Dictionary<uint, uint> orderIds = new Dictionary<uint, uint>();

    public Task<uint> AllocateNextSequenceForAccount(uint accountId)
    {
        return Task.FromResult((uint)1);
    }
}
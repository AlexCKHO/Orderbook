using Trading.Oms.Application.Interfaces;

namespace Trading.Oms.Infrastructure.Services;

public class MockOrderSequenceAllocator : IOrderSequenceAllocator
{
    public Task<uint> AllocateNextSequenceForAccount(uint accountId)
    {
        return Task.FromResult((uint)1);
    }
}
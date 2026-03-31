namespace Trading.Oms.Application.Interfaces;

public interface IOrderSequenceAllocator
{
    
    public Task<uint> AllocateNextSequenceForAccount(uint accountId);
}
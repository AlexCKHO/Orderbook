namespace Trading.Oms.Api.Oms.Domain.Interface;

public interface IOrderIdComposer
{
    ulong Compose(uint accountId, uint sequence);
    
    (uint accountId, uint sequence) Decompose(uint orderId);
}
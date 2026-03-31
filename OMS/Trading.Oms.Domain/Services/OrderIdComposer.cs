namespace Trading.Oms.Api.Oms.Domain.Services;

public class OrderIdComposer
{
    public static ulong ComposeOrderId(uint accountId, uint sequence)
    {
        ulong result = accountId * 10 + sequence;
        return result;
    }
    public static 
}
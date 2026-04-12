using Trading.Oms.Domain.Interface;

namespace Trading.Oms.Domain.Services;

public class OrderIdComposer : IOrderIdComposer
{
    /*
     Example: client order id for account id 1 and order id 1

     Bit: 63 (Mask)   62...32 (Account)   31...0 (Sequence)
       ↓                  ↓                       ↓
       1            00000...0001            00000...0001

       =>  Client Order ID: 9,223,372,041,149,743,105
     */
    public ulong Compose(uint accountId, uint sequence)
    {
        ulong omsMask = 1UL << 63;
        ulong baseId = ((ulong)accountId << 32) | sequence;

        return omsMask | baseId;
    }

    public (uint accountId, uint sequence) Decompose(ulong orderId)
    {
        ulong withoutMask = orderId & ~(1UL << 63);

        uint accountId = (uint)(withoutMask >> 32);

        uint sequence = (uint)orderId;

        return (accountId, sequence);
    }
}
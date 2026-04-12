using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Models;

public class EnginePlaceOrderResult
{
    public Status Status { get; }
    public ulong? OrderId { get; }
    public RejectionCode? RejectionCode { get; }
    public string? RejectionReason { get; }


    public EnginePlaceOrderResult(Status status, ulong? orderId, RejectionCode? rejectionCode = null,
        string? rejectionReason = null)
    {
        Status = status;
        OrderId = orderId;
        RejectionCode = rejectionCode;
        RejectionReason = rejectionReason;
    }
}
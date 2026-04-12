using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Models;

public class EnginePlaceOrderResult
{
    public Status Status { get; }
    public ulong? ClientOrderId { get; }

    public ulong? EngineOrderId { get; }
    public RejectionCode? RejectionCode { get; }
    public string? RejectionReason { get; }


    public EnginePlaceOrderResult(Status status, ulong? clientOrderId,
        ulong? engineOrder,
        RejectionCode? rejectionCode = null,
        string? rejectionReason = null)
    {
        Status = status;
        ClientOrderId = clientOrderId;
        EngineOrderId = engineOrder;
        RejectionCode = rejectionCode;
        RejectionReason = rejectionReason;
    }
}
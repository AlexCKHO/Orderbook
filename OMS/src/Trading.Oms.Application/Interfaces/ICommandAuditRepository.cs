using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Interfaces;

public interface ICommandAuditRepository
{
    public Task InsertReceivedAsync(CommandAudit audit, CancellationToken token);

    public Task UpdateStatusAsync(long auditId, Status status, CancellationToken token);

    public Task MarkCompletedAsync(string requestId, Status engineStatus, long orderId,
        RejectionCode rejectionCode, string rejectionReason, CancellationToken token);
}
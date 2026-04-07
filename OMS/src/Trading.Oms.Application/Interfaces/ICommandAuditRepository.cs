using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Interfaces;

public interface ICommandAuditRepository
{

    public Task InsertReceivedAsync(CommandAudit audit, CancellationToken token);

    public Task MarkCompletedAsync(string requestId, Status engineStatus, long orderId,
        RejectionCode? rejectionCode, string? rejectionReason, DateTimeOffset completedAtUtc, CancellationToken token);

    public Task MarkFailedAsync(string requestId, Status status, string? rejectionReason,
        DateTimeOffset completedAt, CancellationToken token);
}
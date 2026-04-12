using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Interfaces;

public interface ICommandAuditRepository
{
    public Task InsertReceivedAsync(CommandAudit audit, CancellationToken token);

    public Task CompletedAsync(string requestId, Status engineStatus, long clientOrderId, long engineOrderId,
        RejectionCode? rejectionCode, string? rejectionReason, DateTimeOffset completedAtUtc, CancellationToken token);

    public Task MarkFailedAsync(string requestId, Status status, ulong? clientOrderId, string? rejectionReason,
        DateTimeOffset completedAt, CancellationToken token);
}
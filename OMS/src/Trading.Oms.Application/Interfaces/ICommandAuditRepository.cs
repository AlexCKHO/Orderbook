using Trading.Oms.Application.Models;

namespace Trading.Oms.Application.Interfaces;

public interface ICommandAuditRepository
{
    public Task InsertReceivedAsync(CommandAudit audit);

    public Task MarkCompletedAsync(long auditId);

    public Task MarkFailedAsync(long auditId);
}
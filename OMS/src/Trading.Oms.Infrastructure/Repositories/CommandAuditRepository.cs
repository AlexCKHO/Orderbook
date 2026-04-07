using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Infrastructure.Repositories;

public class CommandAuditRepository : ICommandAuditRepository
{
    public Task InsertReceivedAsync(CommandAudit audit)
    {
        throw new NotImplementedException();
    }

    public Task MarkCompletedAsync(long auditId)
    {
        throw new NotImplementedException();
    }

    public Task MarkFailedAsync(long auditId)
    {
        throw new NotImplementedException();
    }
}
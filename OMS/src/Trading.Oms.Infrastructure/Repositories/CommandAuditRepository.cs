using Microsoft.EntityFrameworkCore;
using Trading.Oms.Application.Exceptions;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;
using Trading.Oms.Infrastructure.Persistence;
using Trading.Oms.Infrastructure.Persistence.Entities;

namespace Trading.Oms.Infrastructure.Repositories;

public class CommandAuditRepository(OmsDbContext dbContext) : ICommandAuditRepository
{
    readonly DbSet<CommandAuditEntity> _commandAuditEntitySet = dbContext.Set<CommandAuditEntity>();

    public async Task InsertReceivedAsync(CommandAudit audit, CancellationToken token)
    {
        var record = new CommandAuditEntity
        {
            RequestId = audit.RequestId,
            CorrelationId = audit.CorrelationId,
            IdempotencyKey = audit.IdempotencyKey,
            AccountId = audit.AccountId,
            CommandType = audit.CommandType,
            PayloadHash = audit.PayloadHash,
            RequestPayloadJson = audit.RequestPayloadJson,
            SubmittedAtUtc = audit.SubmittedAtUtc,
            CompletedAtUtc = audit.CompletedAtUtc,
            ClientOrderId = audit.ClientOrderId,
            EngineOrderId = audit.EngineOrderId,
            Status = audit.Status
        };

        try
        {
            _commandAuditEntitySet.Add(record);

            await dbContext.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex)
        {
            throw new CommandAuditConflictException($"Duplicate Idempotency key {ex.Message}");
        }
    }

    public async Task MarkFailedAsync(string requestId, Status status, ulong? clientOrderId, string? rejectionReason,
        DateTimeOffset completedAt, CancellationToken token)
    {
        try
        {
            var result = await GetByRequestIdAsync(requestId, token);

            if (result == null)
            {
                throw new CommandAuditConflictException($"CommandAudit entity {requestId} not found");
            }

            result.ClientOrderId = (long)clientOrderId;
            result.Status = status;
            result.CompletedAtUtc = completedAt;
            result.RejectionReason = rejectionReason;

            _commandAuditEntitySet.Update(result);

            await dbContext.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex)
        {
            throw new CommandAuditConflictException($"Command audit persistence conflict {ex.Message}");
        }
    }

    public async Task<CommandAuditEntity?> GetByRequestIdAsync(string requestId, CancellationToken token)
    {
        return await dbContext.command_audits
            .SingleOrDefaultAsync(x => x.RequestId == requestId, token);
    }

    public async Task CompletedAsync(string requestId, Status engineStatus, long clientOrderId, long engineOrderId,
        RejectionCode? rejectionCode, string? rejectionReason, DateTimeOffset completedAtUtc, CancellationToken token)
    {
        try
        {
            var result = await GetByRequestIdAsync(requestId, token);

            if (result == null)
            {
                throw new CommandAuditConflictException($"CommandAudit request id: {requestId} not found");
            }

            result.ClientOrderId = clientOrderId;
            result.EngineOrderId = engineOrderId;
            result.Status = engineStatus;
            result.RejectionCode = rejectionCode;
            result.RejectionReason = rejectionReason;
            result.CompletedAtUtc = completedAtUtc;

            _commandAuditEntitySet.Update(result);

            await dbContext.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex)
        {
            throw new CommandAuditConflictException($"Duplicate Idempotency key {ex.Message}");
        }
    }
}
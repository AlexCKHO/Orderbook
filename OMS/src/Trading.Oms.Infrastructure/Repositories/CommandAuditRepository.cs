using Microsoft.EntityFrameworkCore;
using Trading.Oms.Application.Exceptions;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;
using Trading.Oms.Infrastructure.Persistence;
using Trading.Oms.Infrastructure.Persistence.Entities;

namespace Trading.Oms.Infrastructure.Repositories;

public class CommandAuditRepository(OmsDbContext _dbContext) : ICommandAuditRepository
{
    readonly DbSet<CommandAuditEntity> _commandAuditEntitySet = _dbContext.Set<CommandAuditEntity>();

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
            OrderId = audit.OrderId,
            Status = audit.Status
        };

        try
        {
            _commandAuditEntitySet.Add(record);

            await _dbContext.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex)
        {
            throw new CommandAuditConflictException($"Duplicate Idempotency key {ex.Message}");
        }
    }

    public async Task MarkFailedAsync(string requestId, Status status, string? rejectionReason,
        DateTimeOffset completedAt, CancellationToken token)
    {
        try
        {
            var result = await _commandAuditEntitySet.FindAsync([requestId], token);

            if (result == null)
            {
                throw new CommandAuditConflictException($"CommandAudit entity {requestId} not found");
            }


            result.Status = status;
            result.CompletedAtUtc = completedAt;
            result.RejectionReason = rejectionReason;

            _commandAuditEntitySet.Update(result);

            await _dbContext.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex)
        {
            throw new CommandAuditConflictException($"Command audit persistence conflict {ex.Message}");
        }
    }

    public async Task<CommandAuditEntity?> GetByRequestIdAsync(string requestId, CancellationToken token)
    {
        return await _dbContext.command_audits
            .SingleOrDefaultAsync(x => x.RequestId == requestId, token);
    }

    public async Task MarkCompletedAsync(string requestId, Status engineStatus, long orderId,
        RejectionCode? rejectionCode, string? rejectionReason, DateTimeOffset completedAtUtc, CancellationToken token)
    {
        try
        {
            var result = await GetByRequestIdAsync(requestId, token);

            if (result == null)
            {
                throw new CommandAuditConflictException($"CommandAudit request id: {requestId} not found");
            }

            result.Status = engineStatus;
            result.OrderId = orderId;
            result.RejectionCode = rejectionCode;
            result.RejectionReason = rejectionReason;
            result.CompletedAtUtc = completedAtUtc;

            _commandAuditEntitySet.Update(result);

            await _dbContext.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex)
        {
            throw new CommandAuditConflictException($"Duplicate Idempotency key {ex.Message}");
        }
    }
}
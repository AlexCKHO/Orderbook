using System.Data;
using Microsoft.EntityFrameworkCore;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;
using Trading.Oms.Infrastructure.Persistence;
using Trading.Oms.Infrastructure.Persistence.Entities;

namespace Trading.Oms.Infrastructure.Repositories;

public class IdempotencyStore(OmsDbContext dbContext, DbSet<IdempotencyRecordEntity> idempotencyRecords)
    : IIdempotencyStore
{
    readonly OmsDbContext _dbContext = dbContext;
    readonly DbSet<IdempotencyRecordEntity> _idempotencyRecordsSet = idempotencyRecords;

    public async Task<IdempotencyRecord?> GetAsync(string scope, uint accountId, string idempotencyKey,
        CancellationToken token)
    {
        var result = await _idempotencyRecordsSet.FindAsync([scope, accountId, idempotencyKey], token);

        if (result is null) return null;

        return new IdempotencyRecord
        {
            Scope = result.Scope,
            AccountId = result.AccountId,
            IdempotencyKey = result.IdempotencyKey,
            RequestId = result.RequestId,
            RequestHash = result.RequestHash,
            State = result.State,
            ResponseStatusCode = result.ResponseStatusCode,
            ResponseJson = result.ResponseJson,
            CreatedAtUtc = result.CreatedAtUtc,
            CompletedAtUtc = result.CreatedAtUtc,
            ExpiresAtUtc = result.CreatedAtUtc
        };
    }

    public void ReserveAsync(IdempotencyReservation reservation, CancellationToken token)
    {
        var record = new IdempotencyRecordEntity
        {
            Scope = reservation.Scope,
            AccountId = reservation.AccountId,
            IdempotencyKey = reservation.IdempotencyKey,
            RequestId = reservation.RequestId,
            RequestHash = reservation.RequestHash,
            State = IdempotencyStates.InProgress,
            ResponseStatusCode = null,
            ResponseJson = null,
            CreatedAtUtc = reservation.CreatedAtUtc,
            CompletedAtUtc = null,
            ExpiresAtUtc = reservation.CreatedAtUtc
        };

        _idempotencyRecordsSet.Add(record);
    }


    public async Task CompleteAsync(string scope, uint accountId, string idempotencyKey, int responseStatusCode,
        string responseJson,
        DateTimeOffset completeAtUtc, CancellationToken token)
    {
        var result = await _idempotencyRecordsSet.FindAsync([scope, accountId, idempotencyKey], token);

        if (result is null) throw new Exception("Idempotency Record not found");

        result.State = IdempotencyStates.Completed;
        result.ResponseStatusCode = responseStatusCode;
        result.ResponseJson = responseJson;
        result.CreatedAtUtc = completeAtUtc;

        await _dbContext.SaveChangesAsync(token);
    }
}
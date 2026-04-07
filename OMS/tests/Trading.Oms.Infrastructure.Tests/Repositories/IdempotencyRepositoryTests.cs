using Microsoft.EntityFrameworkCore;
using Moq;
using Trading.Oms.Application.Exceptions;
using Trading.Oms.Domain.Enums;
using Trading.Oms.Infrastructure.Persistence;
using Trading.Oms.Infrastructure.Persistence.Entities;
using Trading.Oms.Infrastructure.Repositories;
using Trading.Oms.Infrastructure.Tests.Infrastructure;

namespace Trading.Oms.Infrastructure.Tests.Repositories;

public class IdempotencyRepositoryTests
{
    [Test]
    public async Task ReserveAsync_PersistsInProgressRecord()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new IdempotencyRepository(database.Context);
        var reservation = TestData.IdempotencyReservation();

        await repository.ReserveAsync(reservation, CancellationToken.None);

        var saved = await database.Context.idempotency_records.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Scope, Is.EqualTo(reservation.Scope));
            Assert.That(saved.AccountId, Is.EqualTo(reservation.AccountId));
            Assert.That(saved.IdempotencyKey, Is.EqualTo(reservation.IdempotencyKey));
            Assert.That(saved.RequestId, Is.EqualTo(reservation.RequestId));
            Assert.That(saved.RequestHash, Is.EqualTo(reservation.RequestHash));
            Assert.That(saved.State, Is.EqualTo(IdempotencyStates.InProgress));
            Assert.That(saved.ResponseStatusCode, Is.Null);
            Assert.That(saved.ResponseJson, Is.Null);
            Assert.That(saved.CreatedAtUtc, Is.EqualTo(reservation.CreatedAtUtc));
            Assert.That(saved.CompletedAtUtc, Is.Null);
            Assert.That(saved.ExpiresAtUtc, Is.EqualTo(reservation.ExpiresAtUtc));
        });
    }

    [Test]
    public async Task ReserveAsync_WhenSameScopeAccountAndKeyAlreadyExist_ThrowsConflictException()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new IdempotencyRepository(database.Context);
        var reservation = TestData.IdempotencyReservation();
        await repository.ReserveAsync(reservation, CancellationToken.None);
        database.Context.ChangeTracker.Clear();

        var duplicate = TestData.IdempotencyReservation(
            scope: reservation.Scope,
            accountId: reservation.AccountId,
            idempotencyKey: reservation.IdempotencyKey,
            requestId: "request-2");

        var exception = Assert.ThrowsAsync<IdempotencyConflictException>(
            () => repository.ReserveAsync(duplicate, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("Duplicate Idempotency key"));
    }

    [Test]
    public void ReserveAsync_WhenSaveChangesFails_ThrowsConflictExceptionAndUsesDbSet()
    {
        var dbSet = new Mock<DbSet<IdempotencyRecordEntity>>();
        var dbContext = new Mock<OmsDbContext>(new DbContextOptionsBuilder<OmsDbContext>().Options);
        var token = new CancellationTokenSource().Token;
        var reservation = TestData.IdempotencyReservation();

        dbContext.Setup(context => context.Set<IdempotencyRecordEntity>()).Returns(dbSet.Object);
        dbContext
            .Setup(context => context.SaveChangesAsync(token))
            .ThrowsAsync(new DbUpdateException("database failure"));

        var repository = new IdempotencyRepository(dbContext.Object);

        var exception = Assert.ThrowsAsync<IdempotencyConflictException>(
            () => repository.ReserveAsync(reservation, token));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("Duplicate Idempotency key"));
            dbSet.Verify(set => set.Add(It.Is<IdempotencyRecordEntity>(entity =>
                entity.Scope == reservation.Scope &&
                entity.AccountId == reservation.AccountId &&
                entity.IdempotencyKey == reservation.IdempotencyKey &&
                entity.RequestId == reservation.RequestId &&
                entity.State == IdempotencyStates.InProgress)), Times.Once);
            dbContext.Verify(context => context.SaveChangesAsync(token), Times.Once);
        });
    }

    [Test]
    public async Task GetAsync_WhenRecordExists_ReturnsMappedRecord()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        database.Context.idempotency_records.Add(TestData.IdempotencyRecordEntity(
            state: IdempotencyStates.Completed,
            responseStatusCode: 202,
            responseJson: "{\"orderId\":42}",
            completedAtUtc: TestData.CompletedAt));
        await database.Context.SaveChangesAsync();
        var repository = new IdempotencyRepository(database.Context);

        var result = await repository.GetAsync("place-order", 123, "idempotency-1", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Scope, Is.EqualTo("place-order"));
            Assert.That(result.AccountId, Is.EqualTo(123));
            Assert.That(result.IdempotencyKey, Is.EqualTo("idempotency-1"));
            Assert.That(result.RequestId, Is.EqualTo("request-1"));
            Assert.That(result.RequestHash, Is.EqualTo("CAA664181A4BB62365073EF5AE83B8E6328ED5F2B894447ECB1ED9C0791DD9A5"));
            Assert.That(result.State, Is.EqualTo(IdempotencyStates.Completed));
            Assert.That(result.ResponseStatusCode, Is.EqualTo(202));
            Assert.That(result.ResponseJson, Is.EqualTo("{\"orderId\":42}"));
            Assert.That(result.CreatedAtUtc, Is.EqualTo(TestData.SubmittedAt));
            Assert.That(result.CompletedAtUtc, Is.EqualTo(TestData.CompletedAt));
            Assert.That(result.ExpiresAtUtc, Is.EqualTo(TestData.ExpiresAt));
        });
    }

    [Test]
    public async Task GetAsync_WhenRecordDoesNotExist_ReturnsNull()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new IdempotencyRepository(database.Context);

        var result = await repository.GetAsync("place-order", 123, "missing-key", CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CompleteAsync_WhenRecordExists_UpdatesCompletionFields()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        database.Context.idempotency_records.Add(TestData.IdempotencyRecordEntity());
        await database.Context.SaveChangesAsync();
        var repository = new IdempotencyRepository(database.Context);

        await repository.CompleteAsync(
            "place-order",
            123,
            "idempotency-1",
            responseStatusCode: 202,
            responseJson: "{\"status\":\"submitted\"}",
            completeAtUtc: TestData.CompletedAt,
            token: CancellationToken.None);

        var saved = await database.Context.idempotency_records.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved.State, Is.EqualTo(IdempotencyStates.Completed));
            Assert.That(saved.ResponseStatusCode, Is.EqualTo(202));
            Assert.That(saved.ResponseJson, Is.EqualTo("{\"status\":\"submitted\"}"));
            Assert.That(saved.CompletedAtUtc, Is.EqualTo(TestData.CompletedAt));
        });
    }

    [Test]
    public async Task CompleteAsync_WhenRecordDoesNotExist_Throws()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new IdempotencyRepository(database.Context);

        var exception = Assert.ThrowsAsync<Exception>(
            () => repository.CompleteAsync(
                "place-order",
                123,
                "missing-key",
                responseStatusCode: 202,
                responseJson: "{}",
                completeAtUtc: TestData.CompletedAt,
                token: CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("Idempotency Record not found"));
    }

    [Test]
    public async Task FailAsync_WhenRecordExists_UpdatesFailureFields()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        database.Context.idempotency_records.Add(TestData.IdempotencyRecordEntity());
        await database.Context.SaveChangesAsync();
        var repository = new IdempotencyRepository(database.Context);

        await repository.FailAsync(
            "place-order",
            123,
            "idempotency-1",
            responseStatusCode: 500,
            completeAtUtc: TestData.CompletedAt,
            token: CancellationToken.None);

        var saved = await database.Context.idempotency_records.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved.State, Is.EqualTo(IdempotencyStates.Failed));
            Assert.That(saved.ResponseStatusCode, Is.EqualTo(500));
            Assert.That(saved.ResponseJson, Is.Null);
            Assert.That(saved.CompletedAtUtc, Is.EqualTo(TestData.CompletedAt));
        });
    }

    [Test]
    public async Task FailAsync_WhenRecordDoesNotExist_Throws()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new IdempotencyRepository(database.Context);

        var exception = Assert.ThrowsAsync<Exception>(
            () => repository.FailAsync(
                "place-order",
                123,
                "missing-key",
                responseStatusCode: 500,
                completeAtUtc: TestData.CompletedAt,
                token: CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("Idempotency Record not found"));
    }
}

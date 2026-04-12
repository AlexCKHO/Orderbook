using Microsoft.EntityFrameworkCore;
using Moq;
using Trading.Oms.Application.Exceptions;
using Trading.Oms.Domain.Enums;
using Trading.Oms.Infrastructure.Persistence;
using Trading.Oms.Infrastructure.Persistence.Entities;
using Trading.Oms.Infrastructure.Repositories;
using Trading.Oms.Infrastructure.Tests.Infrastructure;

namespace Trading.Oms.Infrastructure.Tests.Repositories;

public class CommandAuditRepositoryTests
{
    [Test]
    public async Task InsertReceivedAsync_PersistsAudit()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new CommandAuditRepository(database.Context);
        var audit = TestData.CommandAudit();

        await repository.InsertReceivedAsync(audit, CancellationToken.None);

        var saved = await database.Context.command_audits.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved.RequestId, Is.EqualTo(audit.RequestId));
            Assert.That(saved.CorrelationId, Is.EqualTo(audit.CorrelationId));
            Assert.That(saved.IdempotencyKey, Is.EqualTo(audit.IdempotencyKey));
            Assert.That(saved.AccountId, Is.EqualTo(audit.AccountId));
            Assert.That(saved.ClientOrderId, Is.EqualTo(audit.ClientOrderId));
            Assert.That(saved.CommandType, Is.EqualTo(audit.CommandType));
            Assert.That(saved.PayloadHash, Is.EqualTo(audit.PayloadHash));
            Assert.That(saved.RequestPayloadJson, Is.EqualTo(audit.RequestPayloadJson));
            Assert.That(saved.SubmittedAtUtc, Is.EqualTo(audit.SubmittedAtUtc));
            Assert.That(saved.CompletedAtUtc, Is.Null);
            Assert.That(saved.Status, Is.EqualTo(Status.Received));
        });
    }

    [Test]
    public async Task InsertReceivedAsync_WhenRequestIdAlreadyExists_ThrowsConflictException()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new CommandAuditRepository(database.Context);
        var audit = TestData.CommandAudit(requestId: "duplicate-request");
        await repository.InsertReceivedAsync(audit, CancellationToken.None);

        var duplicate = TestData.CommandAudit(
            requestId: "duplicate-request",
            correlationId: "correlation-2",
            idempotencyKey: "idempotency-2");

        var exception = Assert.ThrowsAsync<CommandAuditConflictException>(() =>
            repository.InsertReceivedAsync(duplicate, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("Duplicate Idempotency key"));
    }

    [Test]
    public void InsertReceivedAsync_WhenSaveChangesFails_ThrowsConflictExceptionAndUsesDbSet()
    {
        var dbSet = new Mock<DbSet<CommandAuditEntity>>();
        var dbContext = new Mock<OmsDbContext>(new DbContextOptionsBuilder<OmsDbContext>().Options);
        var token = new CancellationTokenSource().Token;
        var audit = TestData.CommandAudit();

        dbContext.Setup(context => context.Set<CommandAuditEntity>()).Returns(dbSet.Object);
        dbContext
            .Setup(context => context.SaveChangesAsync(token))
            .ThrowsAsync(new DbUpdateException("database failure"));

        var repository = new CommandAuditRepository(dbContext.Object);

        var exception =
            Assert.ThrowsAsync<CommandAuditConflictException>(() => repository.InsertReceivedAsync(audit, token));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("Duplicate Idempotency key"));
            dbSet.Verify(set => set.Add(It.Is<CommandAuditEntity>(entity =>
                entity.RequestId == audit.RequestId &&
                entity.CorrelationId == audit.CorrelationId &&
                entity.IdempotencyKey == audit.IdempotencyKey &&
                entity.AccountId == audit.AccountId &&
                entity.Status == audit.Status)), Times.Once);
            dbContext.Verify(context => context.SaveChangesAsync(token), Times.Once);
        });
    }

    [Test]
    public async Task GetByRequestIdAsync_WhenAuditExists_ReturnsAudit()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        database.Context.command_audits.Add(TestData.CommandAuditEntity(requestId: "request-1"));
        await database.Context.SaveChangesAsync();
        var repository = new CommandAuditRepository(database.Context);

        var result = await repository.GetByRequestIdAsync("request-1", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.RequestId, Is.EqualTo("request-1"));
    }

    [Test]
    public async Task GetByRequestIdAsync_WhenAuditDoesNotExist_ReturnsNull()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new CommandAuditRepository(database.Context);

        var result = await repository.GetByRequestIdAsync("missing-request", CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task MarkFailedAsync_WhenAuditExists_UpdatesFailureFields()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        database.Context.command_audits.Add(TestData.CommandAuditEntity(requestId: "request-1"));
        await database.Context.SaveChangesAsync();
        var repository = new CommandAuditRepository(database.Context);

        await repository.MarkFailedAsync(
            "request-1",
            Status.Failed,
            null,
            "engine unavailable",
            TestData.CompletedAt,
            CancellationToken.None);

        var saved = await database.Context.command_audits.SingleAsync(entity => entity.RequestId == "request-1");
        Assert.Multiple(() =>
        {
            Assert.That(saved.Status, Is.EqualTo(Status.Failed));
            Assert.That(saved.CompletedAtUtc, Is.EqualTo(TestData.CompletedAt));
            Assert.That(saved.RejectionReason, Is.EqualTo("engine unavailable"));
            Assert.That(saved.RejectionCode, Is.Null);
            Assert.That(saved.ClientOrderId, Is.Null);
        });
    }

    [Test]
    public async Task MarkFailedAsync_WhenAuditDoesNotExist_ThrowsConflictException()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new CommandAuditRepository(database.Context);

        var exception = Assert.ThrowsAsync<CommandAuditConflictException>(() => repository.MarkFailedAsync(
            "missing-request",
            Status.Failed,
            null,
            "not found",
            TestData.CompletedAt,
            CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("missing-request"));
    }

    [Test]
    public async Task MarkCompletedAsync_WhenAuditExists_UpdatesCompletionFields()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        database.Context.command_audits.Add(TestData.CommandAuditEntity(requestId: "request-1"));
        await database.Context.SaveChangesAsync();
        var repository = new CommandAuditRepository(database.Context);

        await repository.CompletedAsync(
            "request-1",
            Status.Rejected,
            clientOrderId: 987654321,
            engineOrderId: 987654321,
            rejectionCode: RejectionCode.InvalidSymbol,
            rejectionReason: "invalid symbol",
            completedAtUtc: TestData.CompletedAt,
            token: CancellationToken.None);

        var saved = await database.Context.command_audits.SingleAsync(entity => entity.RequestId == "request-1");
        Assert.Multiple(() =>
        {
            Assert.That(saved.Status, Is.EqualTo(Status.Rejected));
            Assert.That(saved.ClientOrderId, Is.EqualTo(987654321));
            Assert.That(saved.RejectionCode, Is.EqualTo(RejectionCode.InvalidSymbol));
            Assert.That(saved.RejectionReason, Is.EqualTo("invalid symbol"));
            Assert.That(saved.CompletedAtUtc, Is.EqualTo(TestData.CompletedAt));
        });
    }

    [Test]
    public async Task MarkCompletedAsync_WhenAuditDoesNotExist_ThrowsConflictException()
    {
        await using var database = await SqliteOmsDbContext.CreateAsync();
        var repository = new CommandAuditRepository(database.Context);

        var exception = Assert.ThrowsAsync<CommandAuditConflictException>(() => repository.CompletedAsync(
            "missing-request",
            Status.Submitted,
            clientOrderId: 987654321,
            engineOrderId: 987654321,
            rejectionCode: null,
            rejectionReason: null,
            completedAtUtc: TestData.CompletedAt,
            token: CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("missing-request"));
    }
}
using Microsoft.EntityFrameworkCore;
using Trading.Oms.Infrastructure.Persistence.Entities;

namespace Trading.Oms.Infrastructure.Persistence;

public class OmsDbContext : DbContext
{
    public OmsDbContext(DbContextOptions<OmsDbContext> options) : base(options)
    {
    }

    public DbSet<CommandAuditEntity> command_audits { get; set; }
    public DbSet<IdempotencyRecordEntity> idempotency_records { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecordEntity>(entity =>
        {
            entity.ToTable("idempotency_records");

            entity.HasKey(e => new { e.Scope, e.AccountId, e.IdempotencyKey });
            entity.Property(e => e.Scope).HasMaxLength(100).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.RequestId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();

            entity.Property(e => e.State)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(e => e.ResponseJson).HasColumnType("jsonb");
            entity.HasIndex(e => e.ExpiresAtUtc).HasDatabaseName("ix_idempotency_records_expires_at_utc");
        });

        modelBuilder.Entity<CommandAuditEntity>(entity =>
        {
            entity.ToTable("command_audits");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .UseIdentityByDefaultColumn();

            entity.HasIndex(e => e.RequestId)
                .IsUnique();

            entity.Property(e => e.RequestId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AccountId).IsRequired();
            entity.Property(e => e.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CommandType)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(e => e.RequestPayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(e => e.RejectionCode)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.RejectionReason).HasMaxLength(255);
            entity.Property(e => e.SubmittedAtUtc).IsRequired();
            entity.HasIndex(e => e.RequestId).IsUnique();
            entity.HasIndex(e => e.CorrelationId);
        });
    }
}
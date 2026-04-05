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
        
        
        modelBuilder.Entity<IdempotencyRecordEntity>().ToTable("idempotency_records");

        modelBuilder.Entity<IdempotencyRecordEntity>()
            .HasKey(ire => new { ire.Scope, ire.AccountId, ire.IdempotencyKey });

        modelBuilder.Entity<IdempotencyRecordEntity>().Property(ire => ire.Scope).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<IdempotencyRecordEntity>().Property(ire => ire.IdempotencyKey).HasMaxLength(100)
            .IsRequired();
        modelBuilder.Entity<IdempotencyRecordEntity>().Property(ire => ire.RequestId).HasMaxLength(50).IsRequired();
        modelBuilder.Entity<IdempotencyRecordEntity>().Property(ire => ire.RequestHash).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<IdempotencyRecordEntity>().Property(e => e.State).HasConversion<string>().HasMaxLength(40)
            .IsRequired();
        modelBuilder.Entity<IdempotencyRecordEntity>().Property(ire => ire.ResponseJson).HasColumnType("jsonb");

        modelBuilder.Entity<IdempotencyRecordEntity>()
            .HasIndex(ire => ire.ExpiresAtUtc).HasDatabaseName("ix_idempotency_records_expires_at_utc");
        
        
        
    }
}
using Microsoft.EntityFrameworkCore;
using Trading.Oms.Api.Oms.Infrastructure.Persistence.Entities;

namespace Trading.Oms.Api.Oms.Infrastructure.Persistence;

public class OmsDbContext : DbContext
{
    public OmsDbContext(DbContextOptions<OmsDbContext> options) : base(options)
    {
    }

    public DbSet<IdempotencyRecordEntity> idempotency_records { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecordEntity>()
            .HasKey(ire => new { ire.Scope, ire.AccountId, ire.IdempotencyKey });
        
        
    }
}
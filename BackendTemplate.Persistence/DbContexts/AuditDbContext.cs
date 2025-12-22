using BackendTemplate.Persistence.Audit;
using Microsoft.EntityFrameworkCore;

namespace BackendTemplate.Persistence.DbContexts;

public sealed class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options)
    {
    }

    public DbSet<DataChangeAuditLog> DataChangeAuditLogs => Set<DataChangeAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DataChangeAuditLog>(b =>
        {
            b.ToTable("data_change_audit_log");
            b.HasKey(x => x.Id);

            b.Property(x => x.OutboxMessageId).IsRequired();

            b.Property(x => x.TraceId).IsRequired();
            b.Property(x => x.EntityType).IsRequired();
            b.Property(x => x.EntityId).IsRequired();
            b.Property(x => x.ChangeType).IsRequired();

            b.HasIndex(x => x.OutboxMessageId).IsUnique();
            b.HasIndex(x => x.TimestampUtc);
            b.HasIndex(x => new { x.EntityType, x.EntityId, x.TimestampUtc });
            b.HasIndex(x => new { x.UserId, x.TimestampUtc });
        });
    }
}

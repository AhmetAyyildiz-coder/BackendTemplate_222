using BackendTemplate.Domain;
using BackendTemplate.Domain.Sample;
using BackendTemplate.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace BackendTemplate.Persistence.DbContexts;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AuditOutboxMessage> AuditOutboxMessages => Set<AuditOutboxMessage>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditOutboxMessage>(b =>
        {
            b.ToTable("audit_outbox");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();

            b.HasIndex(x => x.ProcessedAtUtc);
            b.HasIndex(x => x.LockedUntilUtc);
            b.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.TotalAmount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(3);
        });

        // Mark audited entities on the EF model, based on the [Audited] attribute.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType is null) continue;

            if (Attribute.IsDefined(clrType, typeof(AuditedAttribute)))
                entityType.SetAnnotation("Audit:Enabled", true);
        }
    }
}

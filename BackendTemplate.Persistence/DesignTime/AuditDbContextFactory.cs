using BackendTemplate.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendTemplate.Persistence.DesignTime;

public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var cs =
            Environment.GetEnvironmentVariable("AUDITDB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=backendtemplate_audit;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(cs)
            .Options;

        return new AuditDbContext(options);
    }
}

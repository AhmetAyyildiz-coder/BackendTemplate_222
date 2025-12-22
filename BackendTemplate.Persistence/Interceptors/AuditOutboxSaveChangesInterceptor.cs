using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BackendTemplate.Persistence.Audit;
using BackendTemplate.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendTemplate.Persistence.Interceptors;

public sealed class AuditOutboxSaveChangesInterceptor(Func<string> traceIdAccessor, Func<int?> userIdAccessor)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnqueueAuditOutbox(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnqueueAuditOutbox(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void EnqueueAuditOutbox(DbContext? dbContext)
    {
        if (dbContext is null) return;

        var now = DateTime.UtcNow;
        var traceId = traceIdAccessor();
        var userId = userIdAccessor();

        var entries = dbContext.ChangeTracker
            .Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(IsAudited)
            .ToList();

        if (entries.Count == 0) return;

        foreach (var entry in entries)
        {
            var changeType = entry.State switch
            {
                EntityState.Added => "Create",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => "Unknown"
            };

            var entityType = entry.Metadata.ClrType.Name;
            var entityId = ResolvePrimaryKey(entry);

            var changedProperties = entry.State == EntityState.Modified
                ? entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToList()
                : null;

            var oldValues = entry.State is EntityState.Modified or EntityState.Deleted
                ? ReadValues(entry, current: false, onlyProperties: changedProperties)
                : null;

            var newValues = entry.State is EntityState.Added or EntityState.Modified
                ? ReadValues(entry, current: true, onlyProperties: changedProperties)
                : null;

            var outboxId = Guid.NewGuid();

            var payload = new DataChangeAuditPayload(
                OutboxMessageId: outboxId,
                TraceId: traceId,
                EntityType: entityType,
                EntityId: entityId,
                ChangeType: changeType,
                OldValues: oldValues,
                NewValues: newValues,
                ChangedProperties: changedProperties,
                UserId: userId,
                TimestampUtc: now);

            dbContext.Set<AuditOutboxMessage>().Add(new AuditOutboxMessage
            {
                Id = outboxId,
                CreatedAtUtc = now,
                Type = "DataChange",
                PayloadJson = JsonSerializer.Serialize(payload, JsonOptions)
            });
        }
    }

    private static bool IsAudited(EntityEntry entry)
    {
        var annotation = entry.Metadata.FindAnnotation("Audit:Enabled");
        return annotation?.Value is true;
    }

    private static Dictionary<string, object?> ReadValues(EntityEntry entry, bool current)
        => ReadValues(entry, current, onlyProperties: null);

    private static Dictionary<string, object?> ReadValues(
        EntityEntry entry,
        bool current,
        IReadOnlyCollection<string>? onlyProperties)
    {
        var dict = new Dictionary<string, object?>();

        HashSet<string>? allowed = null;
        if (onlyProperties is { Count: > 0 })
            allowed = onlyProperties.ToHashSet(StringComparer.Ordinal);

        foreach (var prop in entry.Properties)
        {
            if (allowed is not null && !allowed.Contains(prop.Metadata.Name))
                continue;

            if (prop.Metadata.IsShadowProperty())
                continue;

            if (prop.Metadata.IsConcurrencyToken)
                continue;

            dict[prop.Metadata.Name] = current ? prop.CurrentValue : prop.OriginalValue;
        }

        return dict;
    }

    private static string ResolvePrimaryKey(EntityEntry entry)
    {
        var pk = entry.Metadata.FindPrimaryKey();
        if (pk is null || pk.Properties.Count == 0)
            return "";

        if (pk.Properties.Count == 1)
        {
            var prop = pk.Properties[0];
            var value = entry.Property(prop.Name).CurrentValue ?? entry.Property(prop.Name).OriginalValue;
            return value?.ToString() ?? "";
        }

        var parts = pk.Properties
            .Select(p =>
            {
                var v = entry.Property(p.Name).CurrentValue ?? entry.Property(p.Name).OriginalValue;
                return $"{p.Name}={v}";
            });

        return string.Join(";", parts);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

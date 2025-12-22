using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BackendTemplate.Persistence.Audit;
using BackendTemplate.Persistence.DbContexts;
using BackendTemplate.Persistence.Outbox;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendTemplate.Api.Services;

public sealed class AuditOutboxProcessor
{
    private readonly AppDbContext _appDb;
    private readonly AuditDbContext _auditDb;
    private readonly ILogger<AuditOutboxProcessor> _logger;

    public AuditOutboxProcessor(AppDbContext appDb, AuditDbContext auditDb, ILogger<AuditOutboxProcessor> logger)
    {
        _appDb = appDb;
        _auditDb = auditDb;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task ProcessBatchAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var lockOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var lockFor = TimeSpan.FromSeconds(30);

        List<AuditOutboxMessage> messages;

        await using (var tx = await _appDb.Database.BeginTransactionAsync(cancellationToken))
        {
            messages = await _appDb.AuditOutboxMessages
                .Where(x => x.ProcessedAtUtc == null)
                .Where(x => x.LockedUntilUtc == null || x.LockedUntilUtc < now)
                .OrderBy(x => x.CreatedAtUtc)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (messages.Count == 0)
            {
                await tx.CommitAsync(cancellationToken);
                return;
            }

            foreach (var msg in messages)
            {
                msg.LockOwner = lockOwner;
                msg.LockedUntilUtc = now.Add(lockFor);
            }

            await _appDb.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        foreach (var msg in messages)
        {
            try
            {
                if (!string.Equals(msg.Type, "DataChange", StringComparison.Ordinal))
                {
                    await MarkProcessedAsync(msg.Id, cancellationToken);
                    continue;
                }

                var payload = JsonSerializer.Deserialize<DataChangeAuditPayload>(msg.PayloadJson, JsonOptions);
                if (payload is null)
                    throw new InvalidOperationException("Outbox payload could not be deserialized.");

                _auditDb.DataChangeAuditLogs.Add(new DataChangeAuditLog
                {
                    OutboxMessageId = payload.OutboxMessageId,
                    TraceId = payload.TraceId,
                    EntityType = payload.EntityType,
                    EntityId = payload.EntityId,
                    ChangeType = payload.ChangeType,
                    OldValuesJson = payload.OldValues is null ? null : JsonSerializer.Serialize(payload.OldValues, JsonOptions),
                    NewValuesJson = payload.NewValues is null ? null : JsonSerializer.Serialize(payload.NewValues, JsonOptions),
                    ChangedPropertiesJson = payload.ChangedProperties is null ? null : JsonSerializer.Serialize(payload.ChangedProperties, JsonOptions),
                    UserId = payload.UserId,
                    TimestampUtc = payload.TimestampUtc
                });

                await _auditDb.SaveChangesAsync(cancellationToken);
                await MarkProcessedAsync(msg.Id, cancellationToken);
            }
            catch (DbUpdateException dbEx)
            {
                // Likely duplicate due to retry or parallel processing (unique OutboxMessageId)
                _logger.LogWarning(dbEx, "Audit insert conflict for outbox message {OutboxId}. Marking processed.", msg.Id);
                await MarkProcessedAsync(msg.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed processing outbox message {OutboxId}", msg.Id);
                await MarkFailedAsync(msg.Id, ex, cancellationToken);
            }
        }
    }

    private async Task MarkProcessedAsync(Guid outboxId, CancellationToken cancellationToken)
    {
        var msg = await _appDb.AuditOutboxMessages.FirstAsync(x => x.Id == outboxId, cancellationToken);
        msg.ProcessedAtUtc = DateTime.UtcNow;
        msg.LockOwner = null;
        msg.LockedUntilUtc = null;
        msg.LastError = null;
        await _appDb.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(Guid outboxId, Exception ex, CancellationToken cancellationToken)
    {
        var msg = await _appDb.AuditOutboxMessages.FirstAsync(x => x.Id == outboxId, cancellationToken);
        msg.AttemptCount++;
        msg.LastError = ex.Message;

        // Backoff: 1m, 5m, 15m, 1h, 6h...
        var delay = msg.AttemptCount switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            4 => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(6)
        };

        msg.LockOwner = null;
        msg.LockedUntilUtc = DateTime.UtcNow.Add(delay);

        await _appDb.SaveChangesAsync(cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

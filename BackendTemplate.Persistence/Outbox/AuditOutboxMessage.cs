using System;

namespace BackendTemplate.Persistence.Outbox;

public sealed class AuditOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string Type { get; set; } = null!; // e.g. DataChange

    public string PayloadJson { get; set; } = null!;

    public int AttemptCount { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    public string? LockOwner { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public string? LastError { get; set; }
}

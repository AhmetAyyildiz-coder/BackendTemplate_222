using System;

namespace BackendTemplate.Persistence.Audit;

public sealed class DataChangeAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OutboxMessageId { get; set; }

    public string TraceId { get; set; } = null!;

    public string EntityType { get; set; } = null!;

    public string EntityId { get; set; } = null!;

    public string ChangeType { get; set; } = null!; // Create, Update, Delete

    public string? OldValuesJson { get; set; }

    public string? NewValuesJson { get; set; }

    public string? ChangedPropertiesJson { get; set; }

    public int? UserId { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

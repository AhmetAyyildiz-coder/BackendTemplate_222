using System;
using System.Collections.Generic;

namespace BackendTemplate.Persistence.Audit;

public sealed record DataChangeAuditPayload(
    Guid OutboxMessageId,
    string TraceId,
    string EntityType,
    string EntityId,
    string ChangeType,
    Dictionary<string, object?>? OldValues,
    Dictionary<string, object?>? NewValues,
    List<string>? ChangedProperties,
    int? UserId,
    DateTime TimestampUtc
);

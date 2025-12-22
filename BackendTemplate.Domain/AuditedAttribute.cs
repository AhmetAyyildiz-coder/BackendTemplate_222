using System;

namespace BackendTemplate.Domain;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditedAttribute : Attribute
{
    public AuditedAttribute(bool enabled = true) => Enabled = enabled;

    public bool Enabled { get; }
}

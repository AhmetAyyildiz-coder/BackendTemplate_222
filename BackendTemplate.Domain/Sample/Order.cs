using System;

namespace BackendTemplate.Domain.Sample;

[Audited]
public sealed class Order
{
    public Guid Id { get; set; }

    public decimal TotalAmount { get; set; }

    public string Currency { get; set; } = "TRY";
}

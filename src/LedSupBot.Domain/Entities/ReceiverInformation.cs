using LedSupBot.Domain.Common;

namespace LedSupBot.Domain.Entities;

public class ReceiverInformation : Entity
{
    public Guid TechnicalSupportCaseId { get; set; }

    public TechnicalSupportCase TechnicalSupportCase { get; set; } = null!;

    public int Count { get; set; }

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public string? Notes { get; set; }

    public string? RawUtterance { get; set; }

    public double? Confidence { get; set; }
}

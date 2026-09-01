using LedSupBot.Domain.Common;

namespace LedSupBot.Domain.Entities;

/// <summary>
/// Sensitive remote-access identifier. Do not log or expose via public APIs.
/// </summary>
public class AnyDeskInformation : Entity
{
    public Guid TechnicalSupportCaseId { get; set; }

    public TechnicalSupportCase TechnicalSupportCase { get; set; } = null!;

    public string AnyDeskAddress { get; set; } = string.Empty;

    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? RawUtterance { get; set; }
}

using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

public class ConfigConnectionInfo : Entity
{
    public Guid TechnicalSupportCaseId { get; set; }

    public TechnicalSupportCase TechnicalSupportCase { get; set; } = null!;

    public ConfigConnectionStatus Status { get; set; } = ConfigConnectionStatus.NotStarted;

    public bool ExampleImageSent { get; set; }

    public DateTimeOffset? StatusChangedAt { get; set; }

    public string? Notes { get; set; }
}

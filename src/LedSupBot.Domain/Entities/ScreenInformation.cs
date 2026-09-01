using LedSupBot.Domain.Common;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Domain.Entities;

public class ScreenInformation : Entity
{
    public Guid TechnicalSupportCaseId { get; set; }

    public TechnicalSupportCase TechnicalSupportCase { get; set; } = null!;

    public decimal Width { get; set; }

    public decimal Height { get; set; }

    public DimensionUnit Unit { get; set; } = DimensionUnit.Cm;

    /// <summary>Original customer wording before normalization.</summary>
    public string? RawUtterance { get; set; }

    public double? Confidence { get; set; }
}

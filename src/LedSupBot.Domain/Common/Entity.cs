namespace LedSupBot.Domain.Common;

/// <summary>
/// Base identity for aggregate roots and entities. Full entity graphs belong to Phase 3.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    protected void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

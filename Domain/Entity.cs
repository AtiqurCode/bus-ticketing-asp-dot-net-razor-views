namespace BusTicketing.Domain;

/// <summary>
/// Base for every persisted aggregate. Ids are UUIDv7 so they sort by creation
/// time, which keeps the primary-key index from fragmenting.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

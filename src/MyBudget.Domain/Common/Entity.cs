namespace MyBudget.Domain.Common;

/// <summary>
/// Base type for every persisted domain entity.
/// <see cref="CreatedAt"/> and <see cref="UpdatedAt"/> are assigned by the persistence layer
/// (see <c>AuditableEntityInterceptor</c>) as UTC instants.
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// Root entity: the key is assigned immediately so it can be used before the entity is
    /// saved (for example to scope child rows).
    /// </summary>
    protected Entity() => Id = Guid.NewGuid();

    /// <summary>
    /// Record created inside an aggregate navigation.
    /// <para>
    /// Pass <c>true</c> to leave the key unset so the persistence layer generates it.
    /// This is required, not cosmetic: EF Core decides whether an entity discovered through
    /// a navigation is new or existing by checking whether its key is set. A client-assigned
    /// key makes an added child look like an existing row, and the generated statement then
    /// fails as a concurrency conflict.
    /// </para>
    /// </summary>
    protected Entity(bool keyGeneratedByStore)
    {
        if (!keyGeneratedByStore)
        {
            Id = Guid.NewGuid();
        }
    }

    public Guid Id { get; private set; }

    /// <summary>UTC instant at which the row was created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>UTC instant at which the row was last modified.</summary>
    public DateTime UpdatedAt { get; private set; }
}

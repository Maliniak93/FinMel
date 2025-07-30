using FinMel.Domain.Abstractions;

namespace FinMel.Domain.Abstractions;

/// <summary>
/// Base class for all aggregate roots in the domain.
/// An aggregate root is the only member of its aggregate that outside objects are allowed to hold references to.
/// It is responsible for maintaining the integrity of its aggregate and publishing domain events.
/// </summary>
/// <typeparam name="TId">The type of the aggregate root identifier</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id)
    {
    }

    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Gets the collection of domain events that have been raised by this aggregate root.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Raises a domain event by adding it to the collection of domain events.
    /// Domain events represent important business occurrences that other parts of the system may need to react to.
    /// </summary>
    /// <param name="domainEvent">The domain event to raise</param>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

namespace FinMel.Domain.Abstractions;

public abstract record DomainEvent(Guid Id, DateTime OccurredOnUtc) : IDomainEvent;

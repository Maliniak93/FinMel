using FinMel.Domain.Abstractions;

namespace FinMel.Application.Abstractions.Services;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

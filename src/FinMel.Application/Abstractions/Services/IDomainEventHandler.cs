using FinMel.Domain.Abstractions;

namespace FinMel.Application.Abstractions.Services;

public interface IDomainEventHandler
{
    Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
    bool CanHandle(IDomainEvent domainEvent);
}

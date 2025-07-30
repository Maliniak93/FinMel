using FinMel.Application.Abstractions.Services;
using FinMel.Domain.Abstractions;

namespace FinMel.Infrastructure.Services;

public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IEnumerable<IDomainEventHandler> _handlers;

    public DomainEventDispatcher(IEnumerable<IDomainEventHandler> handlers)
    {
        _handlers = handlers;
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlers = _handlers.Where(h => h.CanHandle(domainEvent));
            
            foreach (var handler in handlers)
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
        }
    }
}

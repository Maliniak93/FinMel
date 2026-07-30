using MassTransit;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Identity.Messaging;

/// <summary>
/// Temporary consumer proving <see cref="UserRegistered"/> reaches a real consumer end-to-end
/// through RabbitMQ (T0.10 AC: one trace spanning HTTP request -> outbox publish -> consume).
/// Deliberately does not use the idempotent-inbox template (T0.12, see
/// <c>Skarbiec.ServiceDefaults.Messaging.IdempotentConsumerDefinition{TConsumer,TDbContext}</c> and
/// <c>Messaging/README.md</c>) — every register/login/refresh slice test boots this consumer via
/// <c>IdentityApiFactory</c>, and it's replaced once another service reacts to registration for
/// real (T0.13+) anyway. Real per-service consumers should adopt the template from day one.
/// </summary>
public sealed class UserRegisteredLoggingConsumer(ILogger<UserRegisteredLoggingConsumer> logger) : IConsumer<UserRegistered>
{
    public Task Consume(ConsumeContext<UserRegistered> context)
    {
        logger.LogInformation(
            "Consumed {Event} for user {UserId} ({Email})",
            nameof(UserRegistered), context.Message.UserId, context.Message.Email);

        return Task.CompletedTask;
    }
}

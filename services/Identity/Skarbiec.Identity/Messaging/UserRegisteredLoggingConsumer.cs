using MassTransit;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Identity.Messaging;

/// <summary>
/// Temporary consumer proving <see cref="UserRegistered"/> reaches a real consumer end-to-end
/// through RabbitMQ (T0.10 AC: one trace spanning HTTP request -> outbox publish -> consume).
/// Replace once another service exists to actually react to registration (T0.13+) or the
/// idempotent inbox template lands (T0.12).
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

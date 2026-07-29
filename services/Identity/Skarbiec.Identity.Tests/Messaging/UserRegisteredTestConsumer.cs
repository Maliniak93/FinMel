using MassTransit;
using Microsoft.Extensions.Logging;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Identity.Tests.Messaging;

/// <summary>
/// Temporary consumer proving end-to-end delivery of <see cref="UserRegistered"/> over the real
/// RabbitMQ broker (T0.10 AC) — logs the event and signals <see cref="Received"/>. Stands in for
/// the real consumer(s) a later service adds (T0.13+); not meant to survive past this task.
/// </summary>
public sealed class UserRegisteredTestConsumer(
    TaskCompletionSource<UserRegistered> received,
    ILogger<UserRegisteredTestConsumer> logger) : IConsumer<UserRegistered>
{
    public Task Consume(ConsumeContext<UserRegistered> context)
    {
        logger.LogInformation("Received {Event} for user {UserId}", nameof(UserRegistered), context.Message.UserId);
        received.TrySetResult(context.Message);

        return Task.CompletedTask;
    }
}

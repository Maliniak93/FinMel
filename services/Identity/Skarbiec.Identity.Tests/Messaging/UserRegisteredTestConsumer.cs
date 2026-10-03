using MassTransit;
using Microsoft.Extensions.Logging;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Identity.Tests.Messaging;

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

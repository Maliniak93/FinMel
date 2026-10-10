namespace Skarbiec.Identity.Tests.Messaging;

public sealed class UserRegisteredTestConsumer : IConsumer<UserRegistered>
{
    public Task Consume(ConsumeContext<UserRegistered> context) => Task.CompletedTask;
}

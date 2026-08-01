using Skarbiec.Identity.Tests.Fixtures;
using Skarbiec.Identity.Tests.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.Identity.Tests.Fixtures.IdentityApi;

namespace Skarbiec.Identity.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UserRegisteredDeliveryTests(SkarbiecContainersFixture containers) : IdentityEndpointTests(containers)
{
    [Fact]
    public async Task Register_PublishesUserRegistered_DeliveredAndConsumedOverRabbitMq()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var consumerHost = await TestConsumerHost.StartAsync(RabbitMqConnectionString, cancellationToken);

        using var client = Factory.CreateClient();
        var email = await client.RegisterAsync(cancellationToken);

        var received = await consumerHost.Received.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

        Assert.Equal(email, received.Email);
        Assert.Equal(DisplayName, received.DisplayName);
    }
}

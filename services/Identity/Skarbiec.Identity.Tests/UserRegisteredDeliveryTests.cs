using System.Net;
using System.Net.Http.Json;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Identity.Tests.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UserRegisteredDeliveryTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string RegisterUri = "/api/identity/register";

    private readonly IdentityApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Register_PublishesUserRegistered_DeliveredAndConsumedOverRabbitMq()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var consumerHost = await TestConsumerHost.StartAsync(containers.RabbitMqConnectionString, cancellationToken);

        using var client = _factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Ada Lovelace"
        };

        var response = await client.PostAsJsonAsync(RegisterUri, request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var received = await consumerHost.Received.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

        Assert.Equal(request.Email, received.Email);
        Assert.Equal(request.DisplayName, received.DisplayName);
    }
}

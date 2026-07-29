using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class RegisterEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string RegisterUri = "/api/identity/register";

    private readonly IdentityApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Register_WithValidRequest_ReturnsCreatedAndStoresHashedPassword()
    {
        using var client = _factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Ada Lovelace"
        };

        var response = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(request.Email);

        Assert.NotNull(user);
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(request.Password, user.PasswordHash);
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = "weak",
            DisplayName = "Ada Lovelace"
        };

        var response = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithMalformedEmail_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = "not-an-email",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Ada Lovelace"
        };

        var response = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        using var client = _factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Ada Lovelace"
        };

        var first = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}

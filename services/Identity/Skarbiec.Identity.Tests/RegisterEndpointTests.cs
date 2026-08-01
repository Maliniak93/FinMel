using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Identity.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.Identity.Tests.Fixtures.IdentityApi;

namespace Skarbiec.Identity.Tests;

// Registration is the endpoint under test here, so these facts post to RegisterUri directly
// instead of going through IdentityApi.RegisterAsync — that helper asserts 201 Created, which is
// exactly what the weak-password, malformed-email and duplicate cases below need to observe.
[Collection(TestingDefaults.CollectionName)]
public sealed class RegisterEndpointTests(SkarbiecContainersFixture containers) : IdentityEndpointTests(containers)
{
    [Fact]
    public async Task Register_WithValidRequest_ReturnsCreatedAndStoresHashedPassword()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = Password,
            DisplayName = DisplayName
        };

        var response = await client.PostAsJsonAsync(RegisterUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(request.Email);

        Assert.NotNull(user);
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(request.Password, user.PasswordHash);
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsBadRequest()
    {
        using var client = Factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = "weak",
            DisplayName = DisplayName
        };

        var response = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithMalformedEmail_ReturnsBadRequest()
    {
        using var client = Factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = "not-an-email",
            Password = Password,
            DisplayName = DisplayName
        };

        var response = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = Password,
            DisplayName = DisplayName
        };

        var first = await client.PostAsJsonAsync(RegisterUri, request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(RegisterUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}

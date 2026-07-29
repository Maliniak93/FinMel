using System.Net;
using System.Net.Http.Json;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

// Proves the shared containers fixture actually isolates tests: both facts register the exact
// same e-mail (no Guid uniquification) and both expect success. If the database reset between
// tests didn't work, the second fact to run would see a leftover row from the first and get 409
// Conflict instead of 201 Created (T0.9 AC: "two tests writing the same entity do not interfere").
[Collection(TestingDefaults.CollectionName)]
public sealed class DatabaseIsolationTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string RegisterUri = "/api/identity/register";
    private const string FixedEmail = "isolation-check@example.com";

    private readonly IdentityApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public Task First_RegistrationOfFixedEmail_Succeeds() => RegisterFixedEmailAsync();

    [Fact]
    public Task Second_RegistrationOfSameFixedEmail_AlsoSucceeds() => RegisterFixedEmailAsync();

    private async Task RegisterFixedEmailAsync()
    {
        using var client = _factory.CreateClient();
        var request = new RegisterRequest
        {
            Email = FixedEmail,
            Password = "Str0ng!Passw0rd",
            DisplayName = "Ada Lovelace"
        };

        var response = await client.PostAsJsonAsync(RegisterUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}

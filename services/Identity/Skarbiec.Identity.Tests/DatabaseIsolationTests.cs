using Skarbiec.Identity.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

// Proves the shared containers fixture actually isolates tests: both facts register the exact
// same e-mail (no Guid uniquification) and both expect success. If the database reset between
// tests didn't work, the second fact to run would see a leftover row from the first and get 409
// Conflict instead of 201 Created (T0.9 AC: "two tests writing the same entity do not interfere").
[Collection(TestingDefaults.CollectionName)]
public sealed class DatabaseIsolationTests(SkarbiecContainersFixture containers) : IdentityEndpointTests(containers)
{
    private const string FixedEmail = "isolation-check@example.com";

    [Fact]
    public Task First_RegistrationOfFixedEmail_Succeeds() => RegisterFixedEmailAsync();

    [Fact]
    public Task Second_RegistrationOfSameFixedEmail_AlsoSucceeds() => RegisterFixedEmailAsync();

    private async Task RegisterFixedEmailAsync()
    {
        using var client = Factory.CreateClient();

        // RegisterAsync asserts 201 — the Conflict a broken reset would produce fails right here.
        await client.RegisterAsync(TestContext.Current.CancellationToken, FixedEmail);
    }
}

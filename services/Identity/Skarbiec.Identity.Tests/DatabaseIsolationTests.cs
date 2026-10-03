using Skarbiec.Identity.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

// Both facts register the same e-mail and expect 201, so a broken reset between tests fails one with 409.
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

        await client.RegisterAsync(TestContext.Current.CancellationToken, FixedEmail);
    }
}

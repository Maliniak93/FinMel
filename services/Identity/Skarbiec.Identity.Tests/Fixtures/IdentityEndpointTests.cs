using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests.Fixtures;

/// <summary>
/// Base for Identity's HTTP slice tests. Supplies the test host to
/// <see cref="ServiceEndpointTests{TProgram}"/>, so a test class declares only
/// <c>[Collection(TestingDefaults.CollectionName)]</c> and its facts.
/// </summary>
/// <remarks>
/// The messaging tests (outbox, idempotency, poison message) deliberately don't inherit this —
/// they build their own <c>ServiceProvider</c> and bus rather than booting the HTTP host.
/// </remarks>
public abstract class IdentityEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override IdentityApiFactory Factory { get; } = new(containers);

    /// <summary>
    /// The same broker <see cref="ServiceEndpointTests{TProgram}.Factory"/> publishes to — for tests
    /// that attach their own consumer to assert an event actually left the outbox and was delivered.
    /// </summary>
    protected string RabbitMqConnectionString => containers.RabbitMqConnectionString;
}

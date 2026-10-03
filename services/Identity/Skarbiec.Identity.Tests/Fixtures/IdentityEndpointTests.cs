using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests.Fixtures;

public abstract class IdentityEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override IdentityApiFactory Factory { get; } = new(containers);

    protected string RabbitMqConnectionString => containers.RabbitMqConnectionString;
}

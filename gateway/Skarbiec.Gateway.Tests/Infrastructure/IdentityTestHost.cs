extern alias IdentityAssembly;

using Skarbiec.Testing.Containers;

namespace Skarbiec.Gateway.Tests.Infrastructure;

// A real Kestrel listener, so the Gateway's YARP HttpClient reaches it over a socket.
internal sealed class IdentityTestHost : SkarbiecApiFactory<IdentityAssembly::Program>
{
    public IdentityTestHost(SkarbiecContainersFixture containers) : base(containers, "identity-db")
    {
        UseKestrel(0);
    }
}

extern alias IdentityAssembly;

using Skarbiec.Testing.Containers;

namespace Skarbiec.Gateway.Tests.Infrastructure;

/// <summary>
/// Boots the real Identity service on a real Kestrel listener (not the in-memory TestServer) so
/// the Gateway's own YARP instance — a real outbound <c>HttpClient</c> — can reach it over a real
/// socket, the same way it reaches "identity-service" via Aspire service discovery at runtime.
/// </summary>
internal sealed class IdentityTestHost : SkarbiecApiFactory<IdentityAssembly::Program>
{
    public IdentityTestHost(SkarbiecContainersFixture containers) : base(containers, "identity-db")
    {
        UseKestrel(0);
    }
}

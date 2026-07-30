extern alias PortfolioAssembly;

using Skarbiec.Testing.Containers;

namespace Skarbiec.Gateway.Tests.Infrastructure;

/// <summary>Same rationale as <see cref="IdentityTestHost"/>, for the Portfolio skeleton.</summary>
internal sealed class PortfolioTestHost : SkarbiecApiFactory<PortfolioAssembly::Program>
{
    public PortfolioTestHost(SkarbiecContainersFixture containers) : base(containers, "portfolio-db")
    {
        UseKestrel(0);
    }
}

extern alias PortfolioAssembly;

using Skarbiec.Testing.Containers;

namespace Skarbiec.Gateway.Tests.Infrastructure;

internal sealed class PortfolioTestHost : SkarbiecApiFactory<PortfolioAssembly::Program>
{
    public PortfolioTestHost(SkarbiecContainersFixture containers) : base(containers, "portfolio-db")
    {
        UseKestrel(0);
    }
}

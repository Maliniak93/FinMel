using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class PortfolioApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "portfolio-db");

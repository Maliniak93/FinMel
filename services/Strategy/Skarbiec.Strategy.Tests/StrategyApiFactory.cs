using Skarbiec.Testing.Containers;

namespace Skarbiec.Strategy.Tests;

public sealed class StrategyApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "strategy-db");

using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class MarketDataApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "marketdata-db");

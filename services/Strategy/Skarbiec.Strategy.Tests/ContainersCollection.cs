using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Strategy.Tests;

[CollectionDefinition(TestingDefaults.CollectionName)]
public sealed class ContainersCollection : ICollectionFixture<SkarbiecContainersFixture>;

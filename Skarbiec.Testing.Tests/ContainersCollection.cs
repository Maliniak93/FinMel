using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Tests;

[CollectionDefinition(TestingDefaults.CollectionName)]
public sealed class ContainersCollection : ICollectionFixture<SkarbiecContainersFixture>;

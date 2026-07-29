using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.ServiceDefaults.Tests;

[CollectionDefinition(TestingDefaults.CollectionName)]
public sealed class ContainersCollection : ICollectionFixture<SkarbiecContainersFixture>;

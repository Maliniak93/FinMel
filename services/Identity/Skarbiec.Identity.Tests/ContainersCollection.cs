using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

[CollectionDefinition(TestingDefaults.CollectionName)]
public sealed class ContainersCollection : ICollectionFixture<SkarbiecContainersFixture>;

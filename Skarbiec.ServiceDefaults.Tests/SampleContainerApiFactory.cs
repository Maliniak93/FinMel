using Skarbiec.Testing.Containers;

namespace Skarbiec.ServiceDefaults.Tests;

// The Sample host has no database, so "sample-db" is never read; only SharedTestingReuseTests needs this host.
public sealed class SampleContainerApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "sample-db");

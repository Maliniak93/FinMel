using Skarbiec.Testing.Containers;

namespace Skarbiec.ServiceDefaults.Tests;

// Proves Skarbiec.Testing's base factory + shared containers are reusable from a second,
// independent test project without copy-pasting the Testcontainers/Respawn plumbing (T0.9 AC).
// The Sample host has no database of its own, so "sample-db" is never actually read by the app —
// only SharedTestingReuseTests below (which needs a valid JWT-issuing host) uses this factory;
// the other tests in this project keep using the plain, container-free SampleAppFactory.
public sealed class SampleContainerApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "sample-db");

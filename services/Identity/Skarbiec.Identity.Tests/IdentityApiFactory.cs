using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

public sealed class IdentityApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "identity-db");

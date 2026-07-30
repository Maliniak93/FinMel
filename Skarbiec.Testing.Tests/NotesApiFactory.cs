using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Tests;

public sealed class NotesApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "notes-db");

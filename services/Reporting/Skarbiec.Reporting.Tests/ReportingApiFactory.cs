using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

public sealed class ReportingApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "reporting-db");

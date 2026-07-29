namespace Skarbiec.Testing;

/// <summary>
/// Conventions shared by every service's slice tests.
/// </summary>
public static class TestingDefaults
{
    /// <summary>
    /// xUnit collection name used to share one <see cref="Containers.SkarbiecContainersFixture"/>
    /// across every test class in a service's test project. Each test project still needs its own
    /// <c>[CollectionDefinition(TestingDefaults.CollectionName)]</c> class — xUnit only discovers
    /// collection definitions declared in the assembly under test, not in a referenced one — but the
    /// name is shared so every service wires into the same convention.
    /// </summary>
    public const string CollectionName = "Skarbiec containers";

    /// <summary>
    /// Configuration key <see cref="Containers.SkarbiecApiFactory{TProgram}"/> sets to <c>"true"</c>
    /// on every test host. Services with scheduled/background work (e.g. MarketData's Quartz jobs,
    /// Phase 2) should check this before registering it, so slice tests never race a job.
    /// </summary>
    public const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";
}

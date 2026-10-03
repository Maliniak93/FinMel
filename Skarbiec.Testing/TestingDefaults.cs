namespace Skarbiec.Testing;

public static class TestingDefaults
{
    // Each test project still declares its own [CollectionDefinition]: xUnit discovers only those in the assembly under test.
    public const string CollectionName = "Skarbiec containers";

    // Set on every test host; a service with background work checks it so slice tests never race a job.
    public const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";
}

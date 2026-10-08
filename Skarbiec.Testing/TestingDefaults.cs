namespace Skarbiec.Testing;

public static class TestingDefaults
{
    // A project with a timing-sensitive class declares its own [CollectionDefinition] with DisableParallelization = true: xUnit discovers only those in the assembly under test.
    public const string SerialCollectionName = "Skarbiec serial";

    // Set on every test host; a service with background work checks it so slice tests never race a job.
    public const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";
}

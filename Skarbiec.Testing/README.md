# Skarbiec.Testing

Shared slice-test foundation (T0.9): real PostgreSQL + RabbitMQ via Testcontainers, a
`WebApplicationFactory` base class wired to them, and a JWT helper for tenancy tests.

## Wiring a service's test project into it

1. Reference this project from `services/<Name>/Skarbiec.<Name>.Tests`.
2. Register the containers once per test assembly (an assembly fixture; the project needs no collection definition):

   ```csharp
   [assembly: AssemblyFixture(typeof(SkarbiecContainers))]
   ```

   Every test class then gets its own PostgreSQL database and RabbitMQ vhost through the class fixture
   `SkarbiecContainersFixture` (declared on `ServiceEndpointTests<TProgram>`; add
   `IClassFixture<SkarbiecContainersFixture>` yourself on a class that takes the fixture without it).
   Classes run in parallel, so a class never shares data or queues with another.

3. Subclass `SkarbiecApiFactory<TProgram>` with the service's own DB connection-string name:

   ```csharp
   public sealed class MyServiceApiFactory(SkarbiecContainersFixture containers)
       : SkarbiecApiFactory<Program>(containers, "myservice-db");
   ```

4. In each test class: take
   `SkarbiecContainersFixture` in the constructor, create the factory from it, and implement
   `IAsyncLifetime` to reset the database before every test (xUnit creates a fresh test-class
   instance per `[Fact]`, so this runs before each one — not once per class). Reset through the
   *factory* (`_factory.ResetDatabaseAsync()`), not the containers fixture directly — the factory
   boots its host first (applying migrations), which the raw fixture method can't do, and Respawn
   needs the schema to already exist:

   ```csharp
   public sealed class SomeEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
   {
       private readonly MyServiceApiFactory _factory = new(containers);

       public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());
       public ValueTask DisposeAsync() => _factory.DisposeAsync();

       [Fact]
       public async Task Some_Test() { /* use _factory.CreateClient() */ }
   }
   ```

   A timing-sensitive class (a performance threshold) joins a `[Collection(TestingDefaults.SerialCollectionName)]`
   that the project defines with `[CollectionDefinition(..., DisableParallelization = true)]`; it still gets its
   own database through the class fixture but runs alone.

5. For tenancy tests (T0.14), mint a token for an arbitrary user without registering/logging in:

   ```csharp
   var tokenForUserB = _factory.IssueAccessToken(userB);
   ```

## Why reset per test class instance

The containers are shared for the whole assembly run and each class owns a database (migrated by the first
host that boots on it), but the *data* must not leak between the tests of a class — two facts that both register the same fixed e-mail address must both
succeed. Resetting in the test class's own `IAsyncLifetime.InitializeAsync` achieves that because
xUnit constructs a new test class instance per `[Fact]`.

## Background jobs

`SkarbiecApiFactory<TProgram>` sets `Testing:DisableBackgroundJobs = "true"` on every test host.
Services that register scheduled work (e.g. MarketData's Quartz jobs, Phase 2) should check
`TestingDefaults.DisableBackgroundJobsConfigKey` before scheduling it, so slice tests never race a
background job touching the same data.

## Windows EventLog is off in test hosts

The same factory also sets `Logging:EventLog:LogLevel:Default = "None"`. `WebApplication.CreateBuilder`
registers the Windows EventLog provider by default, and it is the one provider that ignores the
general log levels — it always writes `Warning` and above. A run builds and disposes one host per
`[Fact]` in a single process, and the native handle behind that provider does not survive the first
host's disposal: every later host that logs a warning gets `ObjectDisposedException('EventLogInternal')`
thrown back out of `Logger.Log`. Because it usually surfaces during shutdown (MassTransit logs a
warning when a bus stop runs past its timeout on a loaded broker), the exception escapes
`WebApplicationFactory.DisposeAsync` and fails whichever test happened to own that host — never the
one that caused it. Don't remove the setting; a service needing real event-log output in a test
should register the provider itself.

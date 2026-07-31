using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Proves the xmin concurrency token configured on Asset/Transaction (T1.4 scope: "note on
/// optimistic concurrency ... implement the simple safe option") actually detects a race, instead
/// of two racing SaveChangesAsync calls silently overwriting each other's Asset.Quantity. Drives
/// PortfolioDbContext directly — a genuine race (read, THEN a concurrent write, THEN save) can't
/// be forced through the black-box HTTP client, since each request loads and saves within a single
/// handler call. Uses PortfolioApiFactory only to get the schema migrated (mirrors
/// RemoveAssetEndpointTests.BumpTransactionCountAsync's direct-DbContext technique).
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AssetConcurrencyTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task TwoContextsEditingTheSameAsset_SecondSaveThrowsConcurrencyException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var (portfolioId, assetId) = (Guid.NewGuid(), Guid.NewGuid());

        await using (var seedContext = CreateContext(ownerId))
        {
            seedContext.Portfolios.Add(new PortfolioEntity { Id = portfolioId, Name = "Retirement", Currency = "PLN" });
            seedContext.Assets.Add(new Asset
            {
                Id = assetId,
                PortfolioId = portfolioId,
                AssetClass = AssetClass.Stock,
                Name = "Test stock",
                Currency = "PLN",
                ManualValueDate = new DateOnly(2026, 1, 1)
            });
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        await using var contextA = CreateContext(ownerId);
        await using var contextB = CreateContext(ownerId);
        var assetInA = await contextA.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        var assetInB = await contextB.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);

        // B wins the race, bumping xmin in the database.
        assetInB.Quantity = 5m;
        await contextB.SaveChangesAsync(cancellationToken);

        // A still holds the pre-race xmin as its "original" value.
        assetInA.Quantity = 10m;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextA.SaveChangesAsync(cancellationToken));
    }

    private PortfolioDbContext CreateContext(Guid userId)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new PortfolioDbContext(options, new StubCurrentUser(userId));
    }
}

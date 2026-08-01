using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Proves the xmin concurrency token configured on Asset/Transaction (T1.4 scope: "note on
/// optimistic concurrency ... implement the simple safe option") actually detects a race, instead
/// of two racing SaveChangesAsync calls silently overwriting each other's Asset.Quantity. Drives
/// PortfolioDbContext directly via <see cref="PortfolioEndpointTests.CreateDbContext"/> — a genuine
/// race (read, THEN a concurrent write, THEN save) can't be forced through the black-box HTTP
/// client, since each request loads and saves within a single handler call.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AssetConcurrencyTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task TwoContextsEditingTheSameAsset_SecondSaveThrowsConcurrencyException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var (portfolioId, assetId) = (Guid.NewGuid(), Guid.NewGuid());

        await using (var seedContext = CreateDbContext(ownerId))
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

        await using var contextA = CreateDbContext(ownerId);
        await using var contextB = CreateDbContext(ownerId);
        var assetInA = await contextA.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        var assetInB = await contextB.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);

        // B wins the race, bumping xmin in the database.
        assetInB.Quantity = 5m;
        await contextB.SaveChangesAsync(cancellationToken);

        // A still holds the pre-race xmin as its "original" value.
        assetInA.Quantity = 10m;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextA.SaveChangesAsync(cancellationToken));
    }
}

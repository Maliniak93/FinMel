using Microsoft.EntityFrameworkCore;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

public sealed class PositionBondTermsMigrationTests(SkarbiecContainersFixture containers) : IClassFixture<SkarbiecContainersFixture>
{
    private const string PreviousMigration = "20261105041929_AddPositionOutsideNetWorth";

    [Fact]
    public async Task KeepsExistingPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;", cancellationToken);
            await db.Database.MigrateAsync(PreviousMigration, cancellationToken);

            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "Positions" ("AssetId", "UserId", "AssetClass", "ValuationMode", "Currency", "Quantity", "QuoteUnitsPerQuantity", "IsArchived", "OutsideNetWorth", "Version", "UpdatedAt", "ClassHistory", "QuantityHistory")
                VALUES ({assetId}, {userId}, 4, 1, 'PLN', 5000, 1, false, false, 2, NOW(), '[]'::jsonb, '[]'::jsonb)
                """,
                cancellationToken);

            await db.Database.MigrateAsync(cancellationToken);
        }

        await using var verify = OpenDbContext(containers);
        var position = await verify.Positions.IgnoreQueryFilters().SingleAsync(p => p.AssetId == assetId, cancellationToken);

        Assert.Equal(userId, position.UserId);
        Assert.Equal(5_000m, position.Quantity);
        Assert.Equal(2, position.Version);
        Assert.Null(position.BondTerms);
    }
}

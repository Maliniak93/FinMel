using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class AccountGroupInheritanceMigrationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private const string MigrationBeforeAccountGroupInheritance = "20261111074238_OneOffIncomePlanned";

    private const int Cash = 0;

    private const int Stock = 2;

    private const int Etf = 3;

    private const int BankAccount = 9;

    private static readonly Guid GroupA = Guid.NewGuid();

    private static readonly Guid GroupB = Guid.NewGuid();

    [Fact]
    public async Task AccountTakesCashGroupThenDefault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = await MigrationTestDatabase.CreateAtMigrationAsync(_containers, MigrationBeforeAccountGroupInheritance, cancellationToken);
        var seed = await SeedAsync(db, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        Assert.Equal(GroupA, await ReadAccountGroupAsync(db, seed.DefaultACashA.Account, cancellationToken));
        Assert.Equal(GroupB, await ReadAccountGroupAsync(db, seed.DefaultACashB.Account, cancellationToken));
        Assert.Equal(GroupA, await ReadAccountGroupAsync(db, seed.DefaultACashNone.Account, cancellationToken));
        Assert.Equal(GroupB, await ReadAccountGroupAsync(db, seed.NoDefaultCashB.Account, cancellationToken));
        Assert.Equal(GroupB, await ReadAccountGroupAsync(db, seed.BankBalanceB.Account, cancellationToken));
    }

    [Fact]
    public async Task ComponentsInheritOrBecomeExceptions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = await MigrationTestDatabase.CreateAtMigrationAsync(_containers, MigrationBeforeAccountGroupInheritance, cancellationToken);
        var seed = await SeedAsync(db, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        Assert.Equal((GroupB, false), await ReadAssetAsync(db, seed.DefaultACashB.Cash, cancellationToken));
        Assert.Equal((GroupB, false), await ReadAssetAsync(db, seed.UngroupedStockInGroupB, cancellationToken));
        Assert.Equal((GroupB, false), await ReadAssetAsync(db, seed.EtfWithAccountGroupB, cancellationToken));
        Assert.Equal((GroupA, true), await ReadAssetAsync(db, seed.StockOfOtherGroupA, cancellationToken));
    }

    [Fact]
    public async Task ChangedAssetsAreFlaggedForRepublish()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = await MigrationTestDatabase.CreateAtMigrationAsync(_containers, MigrationBeforeAccountGroupInheritance, cancellationToken);
        var seed = await SeedAsync(db, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        Assert.Equal(GroupA, (await ReadAssetAsync(db, seed.DefaultACashNone.Cash, cancellationToken)).GroupId);
        Assert.True(await ReadPendingAsync(db, seed.DefaultACashNone.Cash, cancellationToken));
        Assert.True(await ReadPendingAsync(db, seed.UngroupedStockInGroupB, cancellationToken));
        Assert.False(await ReadPendingAsync(db, seed.StockOfOtherGroupA, cancellationToken));
        Assert.False(await ReadPendingAsync(db, seed.EtfWithAccountGroupB, cancellationToken));
        Assert.False(await ReadPendingAsync(db, seed.DefaultACashA.Cash, cancellationToken));
        Assert.False(await ReadPendingAsync(db, seed.DefaultACashB.Cash, cancellationToken));
        Assert.False(await ReadPendingAsync(db, seed.NoDefaultCashB.Cash, cancellationToken));
        Assert.False(await ReadPendingAsync(db, seed.BankBalanceB.Cash, cancellationToken));
    }

    private sealed record Seeded(Guid Account, Guid Cash);

    private sealed record Seed(
        Seeded DefaultACashA,
        Seeded DefaultACashB,
        Seeded DefaultACashNone,
        Seeded NoDefaultCashB,
        Seeded BankBalanceB,
        Guid UngroupedStockInGroupB,
        Guid EtfWithAccountGroupB,
        Guid StockOfOtherGroupA);

    private static async Task<Seeded> SeedAccountAsync(
        PortfolioDbContext db,
        Guid userId,
        string name,
        Guid? defaultGroupId,
        Guid? cashGroupId,
        CancellationToken cancellationToken,
        string type = "Brokerage")
    {
        var account = await MigrationTestDatabase.InsertAccountAsync(
            db, userId, name, "Regular", false, cancellationToken, type, defaultGroupId);
        var cash = await MigrationTestDatabase.InsertAssetAsync(
            db, userId, account, $"{name} cash", type == "Bank" ? BankAccount : Cash, cashGroupId, cancellationToken);

        return new Seeded(account, cash);
    }

    private static async Task<Seed> SeedAsync(PortfolioDbContext db, CancellationToken cancellationToken)
    {
        var userId = Guid.NewGuid();
        var defaultACashA = await SeedAccountAsync(db, userId, "Default A cash A", GroupA, GroupA, cancellationToken);
        var defaultACashB = await SeedAccountAsync(db, userId, "Default A cash B", GroupA, GroupB, cancellationToken);
        var defaultACashNone = await SeedAccountAsync(db, userId, "Default A cash none", GroupA, null, cancellationToken);
        var noDefaultCashB = await SeedAccountAsync(db, userId, "No default cash B", null, GroupB, cancellationToken);
        var bankBalanceB = await SeedAccountAsync(db, userId, "Bank", null, GroupB, cancellationToken, "Bank");

        // defaultACashB ends up in group B: its components show all three cases.
        var ungrouped = await MigrationTestDatabase.InsertAssetAsync(
            db, userId, defaultACashB.Account, "Ungrouped stock", Stock, null, cancellationToken);
        var etf = await MigrationTestDatabase.InsertAssetAsync(
            db, userId, defaultACashB.Account, "Etf in account group", Etf, GroupB, cancellationToken);
        var other = await MigrationTestDatabase.InsertAssetAsync(
            db, userId, defaultACashB.Account, "Stock of other group", Stock, GroupA, cancellationToken);

        return new Seed(defaultACashA, defaultACashB, defaultACashNone, noDefaultCashB, bankBalanceB, ungrouped, etf, other);
    }

    private static Task<Guid?> ReadAccountGroupAsync(PortfolioDbContext db, Guid accountId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<Guid?>($"""SELECT "GroupId" AS "Value" FROM "Accounts" WHERE "Id" = {accountId}""").SingleAsync(cancellationToken);

    private static async Task<(Guid? GroupId, bool IsException)> ReadAssetAsync(
        PortfolioDbContext db, Guid assetId, CancellationToken cancellationToken)
    {
        var groupId = await db.Database
            .SqlQuery<Guid?>($"""SELECT "GroupId" AS "Value" FROM "Assets" WHERE "Id" = {assetId}""")
            .SingleAsync(cancellationToken);
        var isException = await db.Database
            .SqlQuery<bool>($"""SELECT "GroupIsException" AS "Value" FROM "Assets" WHERE "Id" = {assetId}""")
            .SingleAsync(cancellationToken);

        return (groupId, isException);
    }

    private static Task<bool> ReadPendingAsync(PortfolioDbContext db, Guid assetId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>($"""SELECT "GroupRepublishPending" AS "Value" FROM "Assets" WHERE "Id" = {assetId}""").SingleAsync(cancellationToken);
}

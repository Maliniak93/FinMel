using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// Base for Portfolio's HTTP slice tests. Supplies the test host to
/// <see cref="ServiceEndpointTests{TProgram}"/>, so a test class declares only
/// <c>[Collection(TestingDefaults.CollectionName)]</c> and its facts.
/// </summary>
public abstract class PortfolioEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override PortfolioApiFactory Factory { get; } = new(containers);

    /// <summary>
    /// A <see cref="PortfolioDbContext"/> scoped to <paramref name="userId"/>, talking to the same
    /// database as <see cref="ServiceEndpointTests{TProgram}.Factory"/>. For the few facts the HTTP
    /// surface can't express — seeding a denormalized counter, or forcing a genuine write race that
    /// a single request handler can never produce.
    /// </summary>
    protected PortfolioDbContext CreateDbContext(Guid userId)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new PortfolioDbContext(options, new StubCurrentUser(userId));
    }

    /// <summary>
    /// asset-transfers-deposit-funding: a funded deposit is exactly as
    /// <see cref="PortfolioApi.CreateFundedDepositAsync"/> left it with its defaults — principal 1 000
    /// from 2026-01-15, Cash at 4 000 of its 5 000, and the two linked legs of 1 000 on the start date.
    /// </summary>
    private protected async Task AssertFundedDepositUnchangedAsync(
        HttpClient client, Guid userId, PortfolioApi.FundedDeposit funded, CancellationToken cancellationToken)
    {
        var deposit = await client.GetDepositAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken);
        Assert.Equal(1_000m, deposit.Principal);
        Assert.Equal(new DateOnly(2026, 1, 15), deposit.StartDate);
        Assert.Equal(4_000m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(1_000m, (await client.GetAssetAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Quantity);

        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Contains(legs, t => t.AssetId == funded.CashAssetId && t.Type == TransactionType.Withdraw);
        Assert.Contains(legs, t => t.AssetId == funded.Deposit.AssetId && t.Type == TransactionType.Deposit);
        Assert.All(legs, leg => Assert.Equal(1_000m, leg.Quantity));
        Assert.All(legs, leg => Assert.Equal(new DateOnly(2026, 1, 15), leg.Date));
        Assert.Equal(1_000m, (await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == funded.Deposit.AssetId, cancellationToken)).Principal);
    }

    /// <summary>
    /// deposit-rollover: everything a rejected deposit write must leave alone — the deposit as
    /// <c>GET .../deposits/{assetId}</c> returns it, <see cref="SnapshotUserRowsAsync"/>, and every
    /// transaction of <paramref name="userId"/> (id, type, quantity, date, frozen rate).
    /// </summary>
    private protected async Task<DepositSnapshot> SnapshotDepositAsync(
        HttpClient client, Guid userId, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var deposit = await client.GetDepositAsync(portfolioId, assetId, cancellationToken);
        var rows = await SnapshotUserRowsAsync(userId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        return new DepositSnapshot(
            deposit,
            rows,
            string.Join(
                Environment.NewLine,
                transactions.Select(t => $"{t.Id}|{t.AssetId}|{t.Type}|{t.Quantity}|{t.Date:O}|{t.FxRateToPln}|{t.TransferId}")));
    }

    /// <summary>deposit-rollover: asserts the deposit, the row counts and every transaction are exactly as <paramref name="before"/>.</summary>
    private protected async Task AssertDepositUnchangedAsync(
        HttpClient client, Guid userId, Guid portfolioId, Guid assetId, DepositSnapshot before, CancellationToken cancellationToken)
    {
        var after = await SnapshotDepositAsync(client, userId, portfolioId, assetId, cancellationToken);
        Assert.Equal(before.Deposit, after.Deposit);
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(before.Transactions, after.Transactions);
    }

    /// <summary>What <see cref="SnapshotDepositAsync"/> captured.</summary>
    private protected sealed record DepositSnapshot(
        DepositResponse Deposit,
        (int Assets, int Transactions, int Terms, decimal TotalQuantity) Rows,
        string Transactions);

    /// <summary>
    /// A snapshot of everything <paramref name="userId"/> owns — asset, transaction and term-deposit
    /// row counts plus the summed asset quantity — so a rejected write can prove "nothing was written"
    /// by comparing the snapshot before and after.
    /// </summary>
    protected async Task<(int Assets, int Transactions, int Terms, decimal TotalQuantity)> SnapshotUserRowsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(userId);
        return (
            await dbContext.Assets.CountAsync(cancellationToken),
            await dbContext.Transactions.CountAsync(cancellationToken),
            await dbContext.Set<TermDeposit>().CountAsync(cancellationToken),
            await dbContext.Assets.SumAsync(a => a.Quantity, cancellationToken));
    }
}

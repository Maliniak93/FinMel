using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests.Fixtures;

public abstract class PortfolioEndpointTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override PortfolioApiFactory Factory { get; } = new(containers);

    protected PortfolioDbContext CreateDbContext(Guid userId)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new PortfolioDbContext(options, new StubCurrentUser(userId));
    }

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

    private protected async Task AssertDepositUnchangedAsync(
        HttpClient client, Guid userId, Guid portfolioId, Guid assetId, DepositSnapshot before, CancellationToken cancellationToken)
    {
        var after = await SnapshotDepositAsync(client, userId, portfolioId, assetId, cancellationToken);
        Assert.Equal(before.Deposit, after.Deposit);
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(before.Transactions, after.Transactions);
    }

    private protected sealed record DepositSnapshot(
        DepositResponse Deposit,
        (int Assets, int Transactions, int Terms, decimal TotalQuantity) Rows,
        string Transactions);

    protected async Task<int> CountSavingsSettlementsAsync(
        Guid userId, CancellationToken cancellationToken, Guid? assetId = null)
    {
        await using var dbContext = CreateDbContext(userId);
        return await dbContext.Set<SavingsInterestSettlement>()
            .CountAsync(s => assetId == null || s.AssetId == assetId, cancellationToken);
    }

    protected async Task<int> CountBondSettlementsAsync(
        Guid userId, CancellationToken cancellationToken, Guid? assetId = null)
    {
        await using var dbContext = CreateDbContext(userId);
        return await dbContext.Set<BondInterestSettlement>()
            .CountAsync(s => assetId == null || s.AssetId == assetId, cancellationToken);
    }

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

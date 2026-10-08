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

    protected async Task<Guid> AddQuotedHoldingAsync(
        HttpClient client,
        Guid portfolioId,
        CancellationToken cancellationToken,
        AssetClass assetClass = AssetClass.Stock,
        string currency = "PLN",
        string name = "Holding",
        string ticker = "TICK",
        decimal? lastPrice = 100m,
        string? exchange = "XETRA",
        DateOnly? lastPriceDate = null)
    {
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithInstrument(instrumentId, assetClass, currency);
        Factory.InstrumentQuoteLookupClient.WithInstrument(instrumentId, ticker, currency, lastPrice, lastPriceDate, exchange, name);

        return await client.AddMarketAssetAsync(portfolioId, cancellationToken, assetClass, currency, instrumentId, name);
    }

    private protected async Task<PortfolioApi.CashAndEtf> CreateCashAndEtfAsync(
        HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 2_000m,
        decimal etfQuantity = 0m,
        string currency = "EUR")
    {
        var cashPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(
            cashPortfolioId, cancellationToken, balance: cashBalance, currency: currency, name: "Broker cash");
        var etfPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Brokerage");
        var etfId = await AddQuotedHoldingAsync(
            client, etfPortfolioId, cancellationToken, AssetClass.Etf, currency, name: "World ETF", ticker: "VWCE.DE");
        if (etfQuantity > 0m)
        {
            await client.RecordTransactionAsync(
                etfPortfolioId, etfId, TransactionType.Buy, etfQuantity, PortfolioApi.DefaultTopUpDate.AddDays(1), cancellationToken, unitPrice: 100m);
        }

        return new PortfolioApi.CashAndEtf(cashPortfolioId, cashId, etfPortfolioId, etfId);
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

    protected async Task<List<BondRedemption>> ReadBondRedemptionsAsync(
        Guid userId, CancellationToken cancellationToken, Guid? assetId = null)
    {
        await using var dbContext = CreateDbContext(userId);
        return await dbContext.Set<BondRedemption>()
            .AsNoTracking()
            .Where(r => assetId == null || r.AssetId == assetId)
            .ToListAsync(cancellationToken);
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

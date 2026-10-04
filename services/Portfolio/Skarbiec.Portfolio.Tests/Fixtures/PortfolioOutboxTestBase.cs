using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchiveAsset;
using Skarbiec.Portfolio.Features.ArchivePortfolio;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;
using Skarbiec.Portfolio.Features.Bonds.UndoBondInterestSettlement;
using Skarbiec.Portfolio.Features.Bonds.UpdateBond;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.DeletePortfolio;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;
using Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestoreAsset;
using Skarbiec.Portfolio.Features.RestorePortfolio;
using Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;
using Skarbiec.Portfolio.Features.SavingsAccounts.SettleSavingsInterest;
using Skarbiec.Portfolio.Features.SavingsAccounts.UndoSavingsInterestSettlement;
using Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Builds its own provider so no hosted service starts and the outbox row is never delivered before the assertion reads it.
[Collection(TestingDefaults.CollectionName)]
public abstract class PortfolioOutboxTestBase(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    protected static readonly Guid UserId = Guid.NewGuid();

    private protected SaveChangesCounter SaveChanges { get; } = new();

    protected ServiceProvider Provider { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Provider = HostlessOutboxProvider.Build<PortfolioDbContext>(containers, services =>
        {
            services.ConfigureDbContext<PortfolioDbContext>(options => options.AddInterceptors(SaveChanges));
            services.AddSingleton<ICurrentUser>(new StubCurrentUser(UserId));
            services.AddSingleton<IInstrumentLookupClient>(new FakeInstrumentLookupClient());
            services.AddSingleton<IFxRateLookupClient>(new FakeFxRateLookupClient());
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<PositionEventPublisher>();
            services.AddScoped<CreatePortfolioHandler>();
            services.AddScoped<AddAssetHandler>();
            services.AddScoped<UpdateAssetHandler>();
            services.AddScoped<RecordTransactionHandler>();
            services.AddScoped<UpdateTransactionHandler>();
            services.AddScoped<DeleteTransactionHandler>();
            services.AddScoped<RemoveAssetHandler>();
            services.AddScoped<AddDepositHandler>();
            services.AddScoped<AddBondHandler>();
            services.AddScoped<UpdateBondHandler>();
            services.AddScoped<SettleBondInterestHandler>();
            services.AddScoped<UndoBondInterestSettlementHandler>();
            services.AddScoped<AddSavingsAccountHandler>();
            services.AddScoped<UpdateSavingsAccountHandler>();
            services.AddScoped<SettleSavingsInterestHandler>();
            services.AddScoped<UndoSavingsInterestSettlementHandler>();
            services.AddScoped<UpdateDepositHandler>();
            services.AddScoped<SettleDepositHandler>();
            services.AddScoped<PayOutDepositHandler>();
            services.AddScoped<RollOverDepositHandler>();
            services.AddScoped<ArchivePortfolioHandler>();
            services.AddScoped<RestorePortfolioHandler>();
            services.AddScoped<ArchiveAssetHandler>();
            services.AddScoped<RestoreAssetHandler>();
            services.AddScoped<DeletePortfolioHandler>();
            services.AddScoped<CreateTransferHandler>();
            services.AddScoped<DeleteTransferHandler>();
        });

        await using (var scope = Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public async ValueTask DisposeAsync() => await Provider.DisposeAsync();

    protected async Task<int> CountPositionEventsAsync(CancellationToken cancellationToken)
    {
        await using var scope = Provider.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
    }

    protected static async Task<Guid> CreatePortfolioAsync(IServiceProvider services, string name, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = name }, cancellationToken);
        Assert.True(result.IsSuccess);

        return result.Value.Id;
    }

    protected static async Task<Guid> AddCashWithBalanceAsync(
        IServiceProvider services, Guid portfolioId, decimal balance, CancellationToken cancellationToken)
    {
        var assetResult = await services.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioId,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash account", Currency = "PLN" },
            cancellationToken);
        Assert.True(assetResult.IsSuccess, assetResult.IsFailure ? assetResult.Error.Code : null);

        var topUp = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            portfolioId,
            assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = balance, UnitPrice = 1m, Date = PortfolioApi.DefaultTopUpDate },
            cancellationToken);
        Assert.True(topUp.IsSuccess, topUp.IsFailure ? topUp.Error.Code : null);

        return assetResult.Value.Id;
    }

    protected static async Task<Guid> AddAssetWithBuyAsync(
        IServiceProvider services, Guid portfolioId, string name, CancellationToken cancellationToken)
    {
        var assetResult = await services.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioId,
            new AddAssetRequest { AssetClass = AssetClass.Stock, Name = name, Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        Assert.True(assetResult.IsSuccess);

        var buyResult = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            portfolioId,
            assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 5m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 2) },
            cancellationToken);
        Assert.True(buyResult.IsSuccess);

        return assetResult.Value.Id;
    }
}

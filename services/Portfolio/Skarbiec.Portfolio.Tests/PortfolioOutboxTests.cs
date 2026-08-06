using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Proves the outbox row and the business row commit atomically for both T1.5 events (mirrors
/// Identity's <c>UserRegisteredOutboxTests</c>, T0.10). Deliberately builds its own
/// <see cref="ServiceProvider"/> instead of using <see cref="PortfolioApiFactory"/>: no
/// <see cref="IHostedService"/> (MassTransit's bus, the outbox delivery poller) is ever started, so
/// the row can never be delivered/removed before the assertion runs.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PortfolioOutboxTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private static readonly Guid UserId = Guid.NewGuid();

    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = HostlessOutboxProvider.Build<PortfolioDbContext>(containers, services =>
        {
            services.AddSingleton<ICurrentUser>(new StubCurrentUser(UserId));
            services.AddSingleton<IInstrumentLookupClient>(new FakeInstrumentLookupClient());
            services.AddScoped<CreatePortfolioHandler>();
            services.AddScoped<AddAssetHandler>();
            services.AddScoped<RecordTransactionHandler>();
        });

        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task AddAsset_WritesAssetChangedOutboxMessageInSameTransactionAsAssetRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        Assert.True(portfolioResult.IsSuccess);

        var handler = scope.ServiceProvider.GetRequiredService<AddAssetHandler>();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Outbox test asset",
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var result = await handler.HandleAsync(portfolioResult.Value.Id, request, cancellationToken);
        Assert.True(result.IsSuccess);

        var asset = await dbContext.Assets.SingleOrDefaultAsync(a => a.Id == result.Value.Id, cancellationToken);
        Assert.NotNull(asset);

        var outboxMessages = await dbContext.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.Contains(outboxMessages, m => m.MessageType.Contains(nameof(AssetChanged)));
    }

    [Fact]
    public async Task RecordTransaction_WritesTransactionRecordedOutboxMessageInSameTransactionAsTransactionRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        Assert.True(portfolioResult.IsSuccess);

        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest
            {
                AssetClass = AssetClass.Stock,
                Name = "Outbox test asset",
                Currency = "PLN",
                ManualValue = 0m,
                ManualValueDate = new DateOnly(2026, 1, 1)
            },
            cancellationToken);
        Assert.True(assetResult.IsSuccess);

        var handler = scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>();
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 5m,
            UnitPrice = 10m,
            Date = new DateOnly(2026, 1, 2)
        };

        var result = await handler.HandleAsync(portfolioResult.Value.Id, assetResult.Value.Id, request, cancellationToken);
        Assert.True(result.IsSuccess);

        var transaction = await dbContext.Transactions.SingleOrDefaultAsync(t => t.Id == result.Value.Id, cancellationToken);
        Assert.NotNull(transaction);

        var outboxMessages = await dbContext.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.Contains(outboxMessages, m => m.MessageType.Contains(nameof(TransactionRecorded)));
    }
}

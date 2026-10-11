using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.ArchiveAccount;
using Skarbiec.Portfolio.Features.Accounts.DeleteAccount;
using Skarbiec.Portfolio.Features.Accounts.RestoreAccount;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class AccountLifecycleOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task ArchiveRestoreAndDelete_PublishNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        var stockId = await AddAssetWithBuyAsync(scope.ServiceProvider, accountId, "Shares", cancellationToken);
        var sold = await scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            stockId,
            new RecordTransactionRequest { Type = TransactionType.Sell, Quantity = 5m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 3) },
            cancellationToken);
        Assert.True(sold.IsSuccess);
        var emptyAccountId = await CreateAccountAsync(scope.ServiceProvider, "Empty", cancellationToken);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var archived = await scope.ServiceProvider.GetRequiredService<ArchiveAccountHandler>().HandleAsync(accountId, cancellationToken);
        var restored = await scope.ServiceProvider.GetRequiredService<RestoreAccountHandler>().HandleAsync(accountId, cancellationToken);
        var deleted = await scope.ServiceProvider.GetRequiredService<DeleteAccountHandler>().HandleAsync(emptyAccountId, cancellationToken);

        Assert.True(archived.IsSuccess);
        Assert.True(restored.IsSuccess);
        Assert.True(deleted.IsSuccess);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
    }
}

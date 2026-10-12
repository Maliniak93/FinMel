using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.DeleteAccount;

public sealed class DeleteAccountHandler(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        var cash = await dbContext.LoadAccountCashAsync(id, cancellationToken);

        var holdsOtherAssets = await dbContext.Assets.AnyAsync(a => a.AccountId == id && a.Id != cash.Id, cancellationToken);
        if (holdsOtherAssets || await dbContext.Transactions.AnyAsync(t => t.AssetId == cash.Id, cancellationToken))
        {
            return AccountErrors.HasHistory(id);
        }

        dbContext.Assets.Remove(cash);
        dbContext.Accounts.Remove(account);

        await publishEndpoint.Publish(new AssetRemoved
        {
            AssetId = cash.Id,
            AccountId = id,
            UserId = dbContext.CurrentUserId,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CashPlaces.DeleteCashPlace;

public sealed class DeleteCashPlaceHandler(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var place = await dbContext.CashPlaces.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return CashPlaceErrors.NotFound(id);
        }

        var balances = await dbContext.LoadBalancesAsync(id, cancellationToken);
        var balanceIds = balances.Select(b => b.Id).ToList();
        if (await dbContext.Transactions.AnyAsync(t => balanceIds.Contains(t.AssetId), cancellationToken))
        {
            return CashPlaceErrors.HasHistory(id);
        }

        dbContext.Assets.RemoveRange(balances);
        dbContext.CashPlaces.Remove(place);

        foreach (var balance in balances)
        {
            await publishEndpoint.Publish(new AssetRemoved
            {
                AssetId = balance.Id,
                AccountId = null,
                UserId = dbContext.CurrentUserId,
                OccurredAtUtc = timeProvider.GetUtcNow()
            }, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

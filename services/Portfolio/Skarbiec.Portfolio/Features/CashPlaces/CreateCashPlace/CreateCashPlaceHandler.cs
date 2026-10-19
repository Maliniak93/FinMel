using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.AssetGroups;

namespace Skarbiec.Portfolio.Features.CashPlaces.CreateCashPlace;

public sealed class CreateCashPlaceHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result<CashPlaceResponse>> HandleAsync(CreateCashPlaceRequest request, CancellationToken cancellationToken)
    {
        var nameTaken = await dbContext.CashPlaces
            .AsNoTracking()
            .AnyAsync(p => p.Name == request.Name, cancellationToken);

        if (nameTaken)
        {
            return CashPlaceErrors.DuplicateName(request.Name);
        }

        if (await dbContext.CheckAsync(request.GroupId, cancellationToken) is { } groupError)
        {
            return groupError;
        }

        var place = new CashPlace { Id = Guid.NewGuid(), Name = request.Name, DefaultGroupId = request.GroupId };
        var balances = request.Currencies.Select(currency => CashPlaceBalances.NewBalance(place, currency)).ToList();

        dbContext.CashPlaces.Add(place);
        dbContext.Assets.AddRange(balances);

        // The place, its balances and their position events commit together.
        foreach (var balance in balances)
        {
            await positionEventPublisher.PublishCreatedAsync(balance, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return place.ToResponse(balances);
    }
}

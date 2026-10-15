using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CashPlaces.AddCashPlaceCurrency;

public sealed class AddCashPlaceCurrencyHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result<CashPlaceResponse>> HandleAsync(Guid id, AddCashPlaceCurrencyRequest request, CancellationToken cancellationToken)
    {
        var place = await dbContext.CashPlaces.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return CashPlaceErrors.NotFound(id);
        }

        if (place.IsArchived)
        {
            return CashPlaceErrors.Archived(id);
        }

        var balances = await dbContext.LoadBalancesAsync(id, cancellationToken);
        if (balances.Any(b => b.Currency == request.Currency))
        {
            return CashPlaceErrors.DuplicateCurrency(request.Currency);
        }

        var balance = CashPlaceBalances.NewBalance(place, request.Currency);
        dbContext.Assets.Add(balance);
        balances.Add(balance);

        await positionEventPublisher.PublishCreatedAsync(balance, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return place.ToResponse(balances);
    }
}

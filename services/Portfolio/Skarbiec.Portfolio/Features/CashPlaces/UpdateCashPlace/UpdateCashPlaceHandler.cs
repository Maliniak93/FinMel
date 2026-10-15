using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CashPlaces.UpdateCashPlace;

public sealed class UpdateCashPlaceHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<CashPlaceResponse>> HandleAsync(Guid id, UpdateCashPlaceRequest request, CancellationToken cancellationToken)
    {
        var place = await dbContext.CashPlaces.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return CashPlaceErrors.NotFound(id);
        }

        if (!string.Equals(place.Name, request.Name, StringComparison.Ordinal))
        {
            var nameTaken = await dbContext.CashPlaces
                .AsNoTracking()
                .AnyAsync(p => p.Id != id && p.Name == request.Name, cancellationToken);

            if (nameTaken)
            {
                return CashPlaceErrors.DuplicateName(request.Name);
            }
        }

        var balances = await dbContext.LoadBalancesAsync(id, cancellationToken);

        // The balances carry the place's name; no event, since AssetPositionChanged holds no name.
        place.Name = request.Name;
        foreach (var balance in balances)
        {
            balance.Name = request.Name;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return place.ToResponse(balances);
    }
}

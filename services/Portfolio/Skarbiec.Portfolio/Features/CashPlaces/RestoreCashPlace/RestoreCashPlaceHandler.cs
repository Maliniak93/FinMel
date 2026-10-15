using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CashPlaces.RestoreCashPlace;

public sealed class RestoreCashPlaceHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<CashPlaceResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var place = await dbContext.CashPlaces.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return CashPlaceErrors.NotFound(id);
        }

        if (place.IsArchived)
        {
            place.IsArchived = false;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.ToResponseAsync(place, cancellationToken);
    }
}

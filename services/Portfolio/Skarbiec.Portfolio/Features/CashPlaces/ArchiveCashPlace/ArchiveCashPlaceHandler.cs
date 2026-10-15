using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CashPlaces.ArchiveCashPlace;

public sealed class ArchiveCashPlaceHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<CashPlaceResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var place = await dbContext.CashPlaces.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return CashPlaceErrors.NotFound(id);
        }

        // Already archived: 200 and nothing to change.
        if (!place.IsArchived)
        {
            var holdsMoney = await dbContext.Assets
                .AnyAsync(a => a.CashPlaceId == id && a.Quantity != 0m, cancellationToken);
            if (holdsMoney)
            {
                return CashPlaceErrors.NotEmpty(id);
            }

            place.IsArchived = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.ToResponseAsync(place, cancellationToken);
    }
}

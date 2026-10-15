using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.CashPlaces.GetCashPlace;

public sealed class GetCashPlaceHandler(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    public async Task<Result<CashPlaceResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var read = await dbContext.ReadAsync(quoteLookup, timeProvider, id, cancellationToken);

        return read.Places.Count == 0 ? CashPlaceErrors.NotFound(id) : read.Places[0];
    }
}

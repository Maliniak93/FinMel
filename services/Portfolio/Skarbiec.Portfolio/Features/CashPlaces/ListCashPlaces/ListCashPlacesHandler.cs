using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.CashPlaces.ListCashPlaces;

public sealed class ListCashPlacesHandler(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    public Task<CashPlacesResponse> HandleAsync(CancellationToken cancellationToken)
        => dbContext.ReadAsync(quoteLookup, timeProvider, placeId: null, cancellationToken);
}

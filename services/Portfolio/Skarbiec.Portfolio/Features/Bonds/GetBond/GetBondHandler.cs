using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Bonds.GetBond;

public sealed class GetBondHandler(PortfolioDbContext dbContext, IBondRateLookupClient rateLookup, TimeProvider timeProvider)
{
    public async Task<Result<BondResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var bond = await dbContext.LoadBondWithEstimateAsync(
            portfolioId, assetId, WarsawCalendar.Today(timeProvider), rateLookup, cancellationToken);

        return bond is null ? BondErrors.NotFound(assetId) : bond;
    }
}

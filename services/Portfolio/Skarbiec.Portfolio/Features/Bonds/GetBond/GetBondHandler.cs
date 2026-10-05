using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds.GetBond;

public sealed class GetBondHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<BondResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var bond = await dbContext.LoadBondAsync(portfolioId, assetId, WarsawCalendar.Today(timeProvider), cancellationToken);

        return bond is null ? BondErrors.NotFound(assetId) : bond;
    }
}

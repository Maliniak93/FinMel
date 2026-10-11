using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Bonds;

internal static class BondTaxExemption
{
    public static bool IsExempt(TaxWrapper wrapper) => wrapper != TaxWrapper.Regular;

    public static async Task<bool> IsTaxExemptAsync(
        this PortfolioDbContext dbContext, Asset bond, CancellationToken cancellationToken)
        => IsExempt(await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == bond.AccountId)
            .Select(a => a.Wrapper)
            .FirstAsync(cancellationToken));
}

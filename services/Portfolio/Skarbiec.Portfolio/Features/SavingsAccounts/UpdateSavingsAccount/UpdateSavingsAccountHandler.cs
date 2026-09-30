using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;

public sealed class UpdateSavingsAccountHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<SavingsAccountResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, UpdateSavingsAccountRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's account, a non-Savings asset and an account under
        // the wrong portfolio all end in 404.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Savings, cancellationToken);
        var terms = asset is null
            ? null
            : await dbContext.SavingsAccounts.FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (asset is null || terms is null)
        {
            return SavingsAccountErrors.NotFound(assetId);
        }

        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .Where(p => p.Id == portfolioId)
            .Select(p => new { p.Name, p.IsArchived })
            .FirstAsync(cancellationToken);

        if (asset.ReadOnlyError(portfolio.IsArchived) is { } readOnly)
        {
            return readOnly;
        }

        asset.Name = request.Name;
        terms.BankName = request.BankName;
        terms.AnnualInterestRatePercent = request.AnnualInterestRatePercent;
        terms.TaxExempt = request.TaxExempt;

        // No AssetPositionChanged: none of these fields is in it, and the balance moves only through
        // the account's transactions.
        await dbContext.SaveChangesAsync(cancellationToken);

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived);
    }
}

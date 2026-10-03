using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.SavingsAccounts;

namespace Skarbiec.Portfolio.Features.ListAssets;

public sealed class ListAssetsHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<AssetResponse>>> HandleAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolioExists = await dbContext.Portfolios
            .AsNoTracking()
            .AnyAsync(p => p.Id == portfolioId, cancellationToken);

        if (!portfolioExists)
        {
            return PortfolioErrors.NotFound(portfolioId);
        }

        // transactionCount as a correlated subquery: no N+1 and no counter column to drift.
        var rows = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.PortfolioId == portfolioId)
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                Asset = a,
                TransactionCount = dbContext.Transactions.Count(t => t.AssetId == a.Id),
                DepositMaturityDate = dbContext.TermDeposits
                    .Where(t => t.AssetId == a.Id)
                    .Select(t => (DateOnly?)t.MaturityDate)
                    .FirstOrDefault(),
                DepositSettled = dbContext.TermDeposits
                    .Where(t => t.AssetId == a.Id)
                    .Select(t => (bool?)(t.SettledOn != null))
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // Savings interest status is computed in memory, costing three more queries only when the page holds a savings account.
        var interest = await dbContext.LoadSavingsInterestStatusAsync(
            [.. rows.Where(r => r.Asset.AssetClass == AssetClass.Savings).Select(r => r.Asset.Id)],
            WarsawCalendar.Today(timeProvider),
            cancellationToken);

        IReadOnlyList<AssetResponse> response = rows
            .Select(r => r.Asset.ToResponse(
                r.TransactionCount,
                r.DepositMaturityDate,
                r.DepositSettled,
                interest.TryGetValue(r.Asset.Id, out var status) ? status.Due is not null : null))
            .ToList();
        return Result<IReadOnlyList<AssetResponse>>.Success(response);
    }
}

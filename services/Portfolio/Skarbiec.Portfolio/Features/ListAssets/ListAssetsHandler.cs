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

        // transactionCount as a correlated subquery inside this one query (spec-02) — no N+1, no
        // second round trip, and no counter column to drift out of sync with the Transactions table.
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

        // The savings accounts' interest status is computed from their daily balances, in memory —
        // three more queries for the whole page, only when it holds a savings account.
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

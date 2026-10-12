using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.SavingsAccounts;

namespace Skarbiec.Portfolio.Features.ListAssets;

public sealed class ListAssetsHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<AssetResponse>>> HandleAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        if (accountId is { } id && !await dbContext.Accounts.AsNoTracking().AnyAsync(a => a.Id == id, cancellationToken))
        {
            return AccountErrors.NotFound(id);
        }

        // transactionCount as a correlated subquery: no N+1 and no counter column to drift.
        var rows = await dbContext.Assets
            .AsNoTracking()
            .Where(a => accountId == null || a.AccountId == accountId)
            // An account's cash is read through the account, never listed as a holding.
            .Where(a => a.AssetClass != AssetClass.Cash || a.AccountId == null)
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
                    .FirstOrDefault(),
                FineWeightGramsPerPiece = dbContext.MetalHoldings
                    .Where(m => m.AssetId == a.Id)
                    .Select(m => (decimal?)m.FineWeightGramsPerPiece)
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
                interest.TryGetValue(r.Asset.Id, out var status) ? status.Due is not null : null,
                r.FineWeightGramsPerPiece))
            .ToList();
        return Result<IReadOnlyList<AssetResponse>>.Success(response);
    }
}

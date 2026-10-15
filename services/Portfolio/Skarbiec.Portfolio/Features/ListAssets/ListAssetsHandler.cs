using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.ListAssets;

public sealed class ListAssetsHandler(PortfolioDbContext dbContext)
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
            // An account's money is read through the account and a place's balances through the place, never listed as holdings.
            .Where(a => a.AssetClass != AssetClass.BankAccount && a.AssetClass != AssetClass.Cash)
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

        IReadOnlyList<AssetResponse> response = rows
            .Select(r => r.Asset.ToResponse(
                r.TransactionCount,
                r.DepositMaturityDate,
                r.DepositSettled,
                r.FineWeightGramsPerPiece))
            .ToList();
        return Result<IReadOnlyList<AssetResponse>>.Success(response);
    }
}

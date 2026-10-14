using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Transfers.ListTransferCandidates;

public sealed class ListTransferCandidatesHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<IReadOnlyList<TransferCandidateResponse>>> HandleAsync(
        string currency, AssetClass assetClass, bool accountCash, CancellationToken cancellationToken)
    {
        var supported = SupportedCurrencies.Validate(currency);
        if (supported.IsFailure)
        {
            return supported.Error;
        }

        IReadOnlyList<TransferCandidateResponse> candidates = await (
                from asset in dbContext.Assets.AsNoTracking()
                where asset.Currency == currency && !asset.IsArchived
                where !dbContext.Accounts.Any(a => a.Id == asset.AccountId && a.IsArchived)
                // The accounts' money is its own list, whatever its class: free Cash never mixes with it.
                where accountCash
                    ? asset.AccountId != null && (asset.AssetClass == AssetClass.Cash || asset.AssetClass == AssetClass.BankAccount)
                    : asset.AssetClass == assetClass && (asset.AssetClass != AssetClass.Cash || asset.AccountId == null)
                orderby asset.Name
                select new TransferCandidateResponse
                {
                    AssetId = asset.Id,
                    Name = asset.Name,
                    Balance = asset.Quantity
                })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<TransferCandidateResponse>>.Success(candidates);
    }
}

using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Transfers.ListTransferCandidates;

public sealed class ListTransferCandidatesHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<IReadOnlyList<TransferCandidateResponse>>> HandleAsync(
        string currency, AssetClass? assetClass, bool accountCash, bool cashPlaces, CancellationToken cancellationToken)
    {
        var supported = SupportedCurrencies.Validate(currency);
        if (supported.IsFailure)
        {
            return supported.Error;
        }

        if (assetClass is null && !accountCash && !cashPlaces)
        {
            return TransferErrors.AssetClassRequired;
        }

        var assets = dbContext.Assets.AsNoTracking()
            .Where(a => a.Currency == currency && !a.IsArchived)
            .Where(a => !dbContext.Accounts.Any(account => account.Id == a.AccountId && account.IsArchived));

        // The accounts' money and the places' balances are their own lists, whatever their class.
        assets = cashPlaces
            ? assets.Where(a => a.CashPlaceId != null && !dbContext.CashPlaces.Any(p => p.Id == a.CashPlaceId && p.IsArchived))
            : accountCash
                ? assets.Where(a => a.AccountId != null && (a.AssetClass == AssetClass.Cash || a.AssetClass == AssetClass.BankAccount))
                : assets.Where(a => a.AssetClass == assetClass && (a.AssetClass != AssetClass.Cash || (a.AccountId == null && a.CashPlaceId == null)));

        IReadOnlyList<TransferCandidateResponse> candidates = await assets
            .OrderBy(a => a.Name)
            .Select(a => new TransferCandidateResponse
            {
                AssetId = a.Id,
                Name = a.Name,
                Balance = a.Quantity
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<TransferCandidateResponse>>.Success(candidates);
    }
}

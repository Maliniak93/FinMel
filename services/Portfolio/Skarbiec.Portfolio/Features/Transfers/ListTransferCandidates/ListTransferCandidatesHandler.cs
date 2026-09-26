using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Transfers.ListTransferCandidates;

public sealed class ListTransferCandidatesHandler(PortfolioDbContext dbContext)
{
    /// <summary>
    /// The pick list of a transfer's counterpart (asset-transfers-deposit-funding): the current user's
    /// assets of <paramref name="assetClass"/> in <paramref name="currency"/>, in non-archived portfolios
    /// only — an archived one is read-only — ordered by portfolio name, then asset name.
    /// </summary>
    public async Task<Result<IReadOnlyList<TransferCandidateResponse>>> HandleAsync(
        string currency, AssetClass assetClass, CancellationToken cancellationToken)
    {
        var supported = SupportedCurrencies.Validate(currency);
        if (supported.IsFailure)
        {
            return supported.Error;
        }

        IReadOnlyList<TransferCandidateResponse> candidates = await (
                from asset in dbContext.Assets.AsNoTracking()
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                where asset.AssetClass == assetClass && asset.Currency == currency && !portfolio.IsArchived
                orderby portfolio.Name, asset.Name
                select new TransferCandidateResponse
                {
                    AssetId = asset.Id,
                    Name = asset.Name,
                    PortfolioId = portfolio.Id,
                    PortfolioName = portfolio.Name,
                    Balance = asset.Quantity
                })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<TransferCandidateResponse>>.Success(candidates);
    }
}

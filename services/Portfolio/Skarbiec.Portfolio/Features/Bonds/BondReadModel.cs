using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds;

internal sealed record BondLinks(
    ILookup<Guid, BondRedemptionResponse> Redemptions, IReadOnlyDictionary<Guid, BondSwapSourceResponse> SwappedFrom);

internal static class BondReadModel
{
    public static async Task<BondResponse?> LoadBondAsync(
        this PortfolioDbContext dbContext, Guid portfolioId, Guid assetId, DateOnly today, CancellationToken cancellationToken)
    {
        // A TreasuryBond row exists only for a Bond-class asset, so any other asset id misses here.
        var row = await (
                from terms in dbContext.TreasuryBonds.AsNoTracking()
                join asset in dbContext.Assets.AsNoTracking() on terms.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                where asset.Id == assetId && asset.PortfolioId == portfolioId
                select new { Terms = terms, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var funding = await dbContext.LoadFundingSourceAsync(assetId, cancellationToken);
        var settlements = await dbContext.BondInterestSettlements
            .AsNoTracking()
            .Where(s => s.AssetId == assetId)
            .ToListAsync(cancellationToken);
        var links = await dbContext.LoadBondLinksAsync([row.Terms], cancellationToken);

        return row.Terms.ToResponse(
            row.Asset,
            row.PortfolioName,
            row.PortfolioIsArchived,
            today,
            funding,
            settlements,
            [.. links.Redemptions[assetId]],
            links.SwappedFrom.GetValueOrDefault(assetId));
    }

    public static async Task<BondLinks> LoadBondLinksAsync(
        this PortfolioDbContext dbContext, IReadOnlyCollection<TreasuryBond> bonds, CancellationToken cancellationToken)
    {
        var ids = bonds.Select(b => b.AssetId).ToList();

        // A counterpart's name is missing once it was removed: its leg is gone, or the asset itself is.
        var redemptions = await (
                from redemption in dbContext.BondRedemptions.AsNoTracking()
                where ids.Contains(redemption.AssetId)
                orderby redemption.Date
                select new
                {
                    redemption.AssetId,
                    redemption.Kind,
                    redemption.Date,
                    redemption.BondCount,
                    redemption.Tax,
                    redemption.Proceeds,
                    DestinationAssetName = (
                            from inLeg in dbContext.Transactions
                            where redemption.CashTransferId != null
                                && inLeg.TransferId == redemption.CashTransferId
                                && inLeg.AssetId != redemption.AssetId
                            join destination in dbContext.Assets on inLeg.AssetId equals destination.Id
                            select destination.Name)
                        .FirstOrDefault(),
                    SwapTargetAssetName = dbContext.Assets
                        .Where(a => a.Id == redemption.SwapTargetAssetId)
                        .Select(a => a.Name)
                        .FirstOrDefault()
                })
            .ToListAsync(cancellationToken);

        var sourceIds = bonds.Where(b => b.SwappedFromAssetId is not null).Select(b => b.SwappedFromAssetId!.Value).ToList();
        var sourceNames = await dbContext.Assets
            .AsNoTracking()
            .Where(a => sourceIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        var swappedFrom = bonds
            .Where(b => b.SwappedFromAssetId is { } sourceId && sourceNames.ContainsKey(sourceId))
            .ToDictionary(
                b => b.AssetId,
                b => new BondSwapSourceResponse { AssetId = b.SwappedFromAssetId!.Value, Name = sourceNames[b.SwappedFromAssetId.Value] });

        return new BondLinks(
            redemptions.ToLookup(
                r => r.AssetId,
                r => new BondRedemptionResponse
                {
                    Kind = r.Kind,
                    Date = r.Date,
                    BondCount = r.BondCount,
                    Tax = r.Tax,
                    Proceeds = r.Proceeds,
                    DestinationAssetName = r.DestinationAssetName,
                    SwapTargetAssetName = r.SwapTargetAssetName
                }),
            swappedFrom);
    }
}

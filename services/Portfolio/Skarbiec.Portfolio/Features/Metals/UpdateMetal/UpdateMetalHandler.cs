using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Metals.UpdateMetal;

public sealed class UpdateMetalHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result<MetalResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, UpdateMetalRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's holding, a non-metal asset and a wrong portfolio all end in 404.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.PreciousMetal, cancellationToken);
        var holding = asset is null
            ? null
            : await dbContext.MetalHoldings.FirstOrDefaultAsync(h => h.AssetId == assetId, cancellationToken);

        if (asset is null || holding is null)
        {
            return MetalErrors.NotFound(assetId);
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
        asset.InstrumentId = MetalInstruments.InstrumentIdFor(request.Metal);
        holding.Metal = request.Metal;
        holding.FineWeightGramsPerPiece = MetalWeight.ToGrams(request.FineWeight, request.WeightUnit);

        // Always published: the instrument or the multiplier may have moved, and Reporting revalues from the next snapshot.
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return holding.ToResponse(asset, portfolio.Name, portfolio.IsArchived);
    }
}

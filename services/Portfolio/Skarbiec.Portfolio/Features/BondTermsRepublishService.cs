using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

public sealed class BondTermsRepublishService(
    IServiceScopeFactory scopeFactory, ILogger<BondTermsRepublishService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var assetIds = await LoadBondAssetIdsAsync(stoppingToken);
            foreach (var assetId in assetIds)
            {
                await RepublishAsync(assetId, stoppingToken);
            }

            logger.LogInformation("BondTermsRepublishService: republished {Count} bond position(s).", assetIds.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BondTermsRepublishService: republishing bond positions failed.");
        }
    }

    private async Task<List<Guid>> LoadBondAssetIdsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        // IgnoreQueryFilters: a startup job with no request user republishes the bonds of every user.
        return await dbContext.Assets
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.AssetClass == AssetClass.Bond && !a.IsArchived)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task RepublishAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        // IgnoreQueryFilters: see above; the event carries the asset's own UserId.
        var asset = await dbContext.Assets
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.Id == assetId, cancellationToken);
        if (asset is null || asset.IsArchived)
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<PositionEventPublisher>().PublishChangedAsync(asset, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

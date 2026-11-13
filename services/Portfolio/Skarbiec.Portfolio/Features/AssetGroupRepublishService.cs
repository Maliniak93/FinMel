using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

public sealed class AssetGroupRepublishService(
    IServiceScopeFactory scopeFactory, ILogger<AssetGroupRepublishService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var assetIds = await LoadPendingAssetIdsAsync(stoppingToken);
            foreach (var assetId in assetIds)
            {
                await RepublishAsync(assetId, stoppingToken);
            }

            logger.LogInformation("AssetGroupRepublishService: republished {Count} asset position(s).", assetIds.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AssetGroupRepublishService: republishing asset positions failed.");
        }
    }

    private async Task<List<Guid>> LoadPendingAssetIdsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        // IgnoreQueryFilters: a startup job with no request user republishes the flagged assets of every user.
        return await dbContext.Assets
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.GroupRepublishPending)
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
            .SingleOrDefaultAsync(a => a.Id == assetId && a.GroupRepublishPending, cancellationToken);
        if (asset is null)
        {
            return;
        }

        asset.GroupRepublishPending = false;
        await scope.ServiceProvider.GetRequiredService<PositionEventPublisher>().PublishChangedAsync(asset, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

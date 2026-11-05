using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources.MfBonds;

namespace Skarbiec.MarketData.Sources;

public sealed record StartupSyncOptions(TimeSpan MinRetryInterval, string FxCron, string PricesCron, string BondCatalogCron);

// Refreshes every kind whose last cron slot passed without a Completed run, one after another, in the background.
public sealed class StartupSyncService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IStartupSyncState state,
    StartupSyncOptions options,
    TimeProvider timeProvider,
    ILogger<StartupSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // After startup, so the Quartz hosted service and the database are up.
            await WaitForApplicationStartedAsync(stoppingToken);

            using var running = state.Begin();
            await FailOrphanedRunsAsync(stoppingToken);

            foreach (var (kind, cron) in new[]
            {
                (SyncRunKind.Fx, options.FxCron),
                (SyncRunKind.Prices, options.PricesCron),
                (SyncRunKind.BondCatalog, options.BondCatalogCron),
            })
            {
                await SyncIfStaleAsync(kind, cron, stoppingToken);
            }

            await ReconcileCoverageAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The startup sync failed.");
        }
    }

    private async Task FailOrphanedRunsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var now = timeProvider.GetUtcNow();

        var failed = await db.SyncRuns
            .Where(r => r.Status == SyncRunStatus.Running)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, SyncRunStatus.Failed).SetProperty(r => r.FinishedAt, now),
                cancellationToken);

        if (failed > 0)
        {
            logger.LogWarning("{Count} sync run(s) left Running by a previous process were marked Failed.", failed);
        }
    }

    private async Task SyncIfStaleAsync(SyncRunKind kind, string cron, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<MarketDataDbContext>();

            var runs = db.SyncRuns.AsNoTracking().Where(r => r.Kind == kind);
            var lastCompletedStart = await runs
                .Where(r => r.Status == SyncRunStatus.Completed)
                .MaxAsync(r => (DateTimeOffset?)r.StartedAt, cancellationToken);
            var latestStart = await runs.MaxAsync(r => (DateTimeOffset?)r.StartedAt, cancellationToken);

            if (!SyncFreshness.IsDue(cron, timeProvider.GetUtcNow(), lastCompletedStart, latestStart, options.MinRetryInterval))
            {
                logger.LogInformation("Startup sync: {Kind} is up to date or was tried recently; skipped.", kind);
                return;
            }

            logger.LogInformation("Startup sync: {Kind} is stale; running.", kind);
            switch (kind)
            {
                case SyncRunKind.Fx:
                    await ActivatorUtilities.CreateInstance<FxSyncJob>(services).RunAsync(cancellationToken, SyncRunSource.Startup);
                    break;
                case SyncRunKind.Prices:
                    await ActivatorUtilities.CreateInstance<PriceSyncJob>(services).RunAsync(cancellationToken, SyncRunSource.Startup);
                    break;
                case SyncRunKind.BondCatalog:
                    await ActivatorUtilities.CreateInstance<BondCatalogSyncJob>(services).RunAsync(cancellationToken, SyncRunSource.Startup);
                    break;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Startup sync of {Kind} failed.", kind);
        }
    }

    private async Task ReconcileCoverageAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<HistoryCoverageReconciler>().ReconcileAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Startup history coverage reconcile failed.");
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(cancellationToken);
    }
}

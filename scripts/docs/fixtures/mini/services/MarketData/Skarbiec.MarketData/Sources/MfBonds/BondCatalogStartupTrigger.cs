namespace Skarbiec.MarketData.Sources.MfBonds;

public sealed class BondCatalogStartupTrigger(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}

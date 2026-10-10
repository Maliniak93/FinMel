namespace Skarbiec.MarketData.Sources.Yahoo;

public sealed class YahooPriceSource(IYahooApiClient client) : IPriceSource
{
    public Task FetchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

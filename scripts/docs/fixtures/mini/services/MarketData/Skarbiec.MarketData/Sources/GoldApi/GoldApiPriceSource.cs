namespace Skarbiec.MarketData.Sources.GoldApi;

public sealed class GoldApiPriceSource(IGoldApiClient client) : IPriceSource
{
    public Task FetchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

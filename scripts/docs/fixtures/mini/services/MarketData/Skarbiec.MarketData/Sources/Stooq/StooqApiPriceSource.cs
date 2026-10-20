namespace Skarbiec.MarketData.Sources.StooqApi;

public sealed class StooqApiPriceSource(IStooqApiClient client) : IPriceSource
{
    public Task FetchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

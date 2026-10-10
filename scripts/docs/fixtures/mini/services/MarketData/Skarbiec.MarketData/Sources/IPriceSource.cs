namespace Skarbiec.MarketData.Sources;

public interface IPriceSource
{
    Task FetchAsync(CancellationToken cancellationToken);
}

namespace Skarbiec.MarketData.Sources.Yahoo;

// Returns the raw chart body on 2xx and on 404, since Yahoo explains an unknown symbol inside a 404 body.
public interface IYahooApiClient
{
    Task<string> GetLatestAsync(string ticker, CancellationToken cancellationToken);

    Task<string> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

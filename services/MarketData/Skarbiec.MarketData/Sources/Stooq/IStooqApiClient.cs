namespace Skarbiec.MarketData.Sources.Stooq;

// No 404 translation: Stooq signals no data inside a 200 OK body.
public interface IStooqApiClient
{
    // One request per ticker, so one bad ticker never fails the batch.
    Task<string> GetLatestAsync(string ticker, CancellationToken cancellationToken);

    Task<string> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

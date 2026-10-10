namespace Skarbiec.Portfolio.Quotes;

public sealed class QuotesApiClient(HttpClient httpClient)
{
    public Task<string> GetQuoteAsync(string symbol, CancellationToken cancellationToken) =>
        httpClient.GetStringAsync($"quotes/{symbol}", cancellationToken);
}

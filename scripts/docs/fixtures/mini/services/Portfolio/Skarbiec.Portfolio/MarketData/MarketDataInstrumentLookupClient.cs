namespace Skarbiec.Portfolio.MarketData;

public sealed class MarketDataInstrumentLookupClient(HttpClient http)
{
    public async Task LookupAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/internal/instruments/batch", ids, cancellationToken);
    }
}

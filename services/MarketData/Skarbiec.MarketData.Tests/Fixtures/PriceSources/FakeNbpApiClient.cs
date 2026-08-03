using Skarbiec.MarketData.Sources.Nbp;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>
/// Canned <see cref="INbpApiClient"/> driven by a recorded response, a "no data" (404) stand-in, or
/// a thrown transport exception — the same "swap the fetch, exercise the real parse logic through
/// the public interface" pattern T2.2's <see cref="FixturePriceSource"/> established, applied to the
/// real <see cref="NbpPriceSource"/>/<see cref="NbpFxRateSource"/> instead of a throwaway double.
/// Also counts range-query calls so history-backfill chunking (T2.3 scope) is verifiable.
/// </summary>
public sealed class FakeNbpApiClient : INbpApiClient
{
    private readonly string? _rawResponse;
    private readonly Exception? _throwOnRequest;

    private FakeNbpApiClient(string? rawResponse, Exception? throwOnRequest)
    {
        _rawResponse = rawResponse;
        _throwOnRequest = throwOnRequest;
    }

    public static FakeNbpApiClient WithResponse(string rawResponse) => new(rawResponse, throwOnRequest: null);

    /// <summary>Mirrors <see cref="NbpApiClient"/> translating a live 404 (non-trading day) to <see langword="null"/>.</summary>
    public static FakeNbpApiClient NoData() => new(rawResponse: null, throwOnRequest: null);

    public static FakeNbpApiClient ThrowingOnRequest(Exception exception) => new(rawResponse: null, throwOnRequest: exception);

    public int RangeRequestCount { get; private set; }

    public Task<string?> GetTableAAsync(DateOnly? date, CancellationToken cancellationToken) => Respond();

    public Task<string?> GetTableARangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        RangeRequestCount++;
        return Respond();
    }

    public Task<string?> GetGoldPriceAsync(DateOnly? date, CancellationToken cancellationToken) => Respond();

    public Task<string?> GetGoldPriceRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        RangeRequestCount++;
        return Respond();
    }

    private Task<string?> Respond() =>
        _throwOnRequest is not null ? Task.FromException<string?>(_throwOnRequest) : Task.FromResult(_rawResponse);
}

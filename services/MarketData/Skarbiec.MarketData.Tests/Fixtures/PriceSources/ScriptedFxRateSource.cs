using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

public sealed class ScriptedFxRateSource(
    PriceFetchResult<FxRateQuote>? latestResult = null,
    PriceFetchResult<FxRateQuote>? historyResult = null,
    IReadOnlyDictionary<string, PriceFetchResult<FxRateQuote>>? historyResultsByCode = null) : IFxRateSource
{
    private readonly List<HistoryFetch> _historyFetches = [];

    public TimeSpan RequestDelay => TimeSpan.Zero;

    // The scripted result ignores the arguments, so a range assertion reads them from here.
    public IReadOnlyList<HistoryFetch> HistoryFetches => _historyFetches;

    public int HistoryFetchCount => _historyFetches.Count;

    public Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
        IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken)
    {
        if (latestResult is null)
        {
            throw new NotSupportedException($"This {nameof(ScriptedFxRateSource)} wasn't given a latestResult.");
        }

        return Task.FromResult(latestResult);
    }

    public Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        _historyFetches.Add(new HistoryFetch(currencyCode, from, to));

        // A per-currency override wins, so one currency can fail while the others succeed.
        if (historyResultsByCode is not null && historyResultsByCode.TryGetValue(currencyCode, out var scripted))
        {
            return Task.FromResult(scripted);
        }

        if (historyResult is null)
        {
            throw new NotSupportedException($"This {nameof(ScriptedFxRateSource)} wasn't given a historyResult.");
        }

        return Task.FromResult(historyResult);
    }

    public sealed record HistoryFetch(string CurrencyCode, DateOnly From, DateOnly To);
}

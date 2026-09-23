using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>FX counterpart to <see cref="ScriptedPriceSource"/> — see its doc comment.</summary>
public sealed class ScriptedFxRateSource(
    PriceFetchResult<FxRateQuote>? latestResult = null,
    PriceFetchResult<FxRateQuote>? historyResult = null,
    IReadOnlyDictionary<string, PriceFetchResult<FxRateQuote>>? historyResultsByCode = null) : IFxRateSource
{
    private readonly List<HistoryFetch> _historyFetches = [];

    public TimeSpan RequestDelay => TimeSpan.Zero;

    /// <summary>Every <see cref="FetchHistoryAsync"/> call with the arguments the caller actually passed —
    /// the scripted result ignores them, so a range or per-currency assertion must read them from here
    /// rather than from the canned data.</summary>
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

        // Per-currency override (FxSyncJobTests' backfill-isolation AC) takes priority over the
        // single shared historyResult below, so one currency can be scripted to fail while every
        // other currency in the same run still succeeds.
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

using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Tests.Fixtures;

public sealed class FakePriceQuoteClient : IPriceQuoteClient
{
    private readonly Dictionary<Guid, InstrumentPriceLookup> _prices = [];
    private readonly Dictionary<string, FxRateLookup> _fxRates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, List<InstrumentPriceLookup>> _priceHistory = [];
    private readonly Dictionary<string, List<FxRateLookup>> _fxHistory = new(StringComparer.OrdinalIgnoreCase);
    private int _historyCalls;
    private bool _unavailable;
    private Func<Task>? _historyHook;

    public int HistoryCalls => Volatile.Read(ref _historyCalls);

    public FakePriceQuoteClient WithPrice(Guid instrumentId, InstrumentPriceLookup price)
    {
        _prices[instrumentId] = price;
        return this;
    }

    public FakePriceQuoteClient WithFxRate(string pair, FxRateLookup rate)
    {
        _fxRates[pair] = rate;
        return this;
    }

    public FakePriceQuoteClient WithPriceHistory(
        Guid instrumentId, string quoteCurrency, params (DateOnly Date, decimal Close)[] points)
    {
        _priceHistory[instrumentId] = [.. points.OrderBy(p => p.Date).Select(p => new InstrumentPriceLookup(quoteCurrency, p.Date, p.Close))];
        return this;
    }

    public FakePriceQuoteClient WithFxHistory(string pair, params (DateOnly Date, decimal Rate)[] points)
    {
        _fxHistory[pair] = [.. points.OrderBy(p => p.Date).Select(p => new FxRateLookup(p.Date, p.Rate))];
        return this;
    }

    public FakePriceQuoteClient Unavailable()
    {
        _unavailable = true;
        return this;
    }

    // Runs after a history fetch succeeded, before it returns: lets a test change the database mid-rebuild.
    public FakePriceQuoteClient OnHistoryFetched(Func<Task> hook)
    {
        _historyHook = hook;
        return this;
    }

    public Task<IReadOnlyDictionary<Guid, InstrumentPriceLookup>> GetLatestPricesAsync(
        IReadOnlyList<Guid> instrumentIds, DateOnly asOfDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, InstrumentPriceLookup>>(
            _prices.Where(p => instrumentIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));

    public Task<IReadOnlyDictionary<string, FxRateLookup>> GetLatestFxRatesAsync(
        IReadOnlyList<string> pairs, DateOnly asOfDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, FxRateLookup>>(
            _fxRates.Where(r => pairs.Contains(r.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase));

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<InstrumentPriceLookup>>> GetPriceHistoryAsync(
        IReadOnlyList<Guid> instrumentIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await EnterHistoryCallAsync();

        IReadOnlyDictionary<Guid, IReadOnlyList<InstrumentPriceLookup>> result = _priceHistory
            .Where(s => instrumentIds.Contains(s.Key))
            .ToDictionary(s => s.Key, s => Window(s.Value, p => p.Date, from, to));

        await RunHookAsync();
        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FxRateLookup>>> GetFxHistoryAsync(
        IReadOnlyList<string> pairs, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await EnterHistoryCallAsync();

        IReadOnlyDictionary<string, IReadOnlyList<FxRateLookup>> result = _fxHistory
            .Where(s => pairs.Contains(s.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(s => s.Key, s => Window(s.Value, r => r.Date, from, to), StringComparer.OrdinalIgnoreCase);

        await RunHookAsync();
        return result;
    }

    // Mirrors MarketData: the latest element before from, then every element in from..to.
    private static IReadOnlyList<T> Window<T>(List<T> series, Func<T, DateOnly> date, DateOnly from, DateOnly to)
    {
        var before = series.Where(e => date(e) < from).TakeLast(1);
        var inRange = series.Where(e => date(e) >= from && date(e) <= to);
        return [.. before.Concat(inRange)];
    }

    private Task EnterHistoryCallAsync()
    {
        Interlocked.Increment(ref _historyCalls);
        return _unavailable ? throw new HttpRequestException("MarketData is unavailable.") : Task.CompletedTask;
    }

    private Task RunHookAsync() => _historyHook is null ? Task.CompletedTask : _historyHook();
}

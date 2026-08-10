using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>
/// <see cref="IPriceSource"/> whose <see cref="FetchLatestAsync"/> blocks on an externally-controlled
/// <paramref name="gate"/> until the test releases it — widens a <see cref="PriceSyncJob"/> run's
/// execution window on demand instead of racing against incidental timing, e.g. to deterministically
/// prove a second manual trigger sees the first run still in flight (T2.14's double-click AC).
/// </summary>
public sealed class GatedPriceSource(
    PriceSource source, TaskCompletionSource gate, PriceFetchResult<InstrumentQuote> result) : IPriceSource
{
    public PriceSource Source { get; } = source;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public async Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        await gate.Task.WaitAsync(cancellationToken);
        return result;
    }

    public Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"This {nameof(GatedPriceSource)} only supports {nameof(FetchLatestAsync)}.");
}

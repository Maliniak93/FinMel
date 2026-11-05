using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Features.GetFxRate;

// The same "last rate on or before" rule as GetFxRatesBatch and Reporting's valuation.
public sealed class GetFxRateHandler
{
    public static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(5);

    private const int ReadThroughWindowDays = 14;

    private readonly MarketDataDbContext dbContext;
    private readonly IFxRateSource fxRateSource;
    private readonly ILogger<GetFxRateHandler> logger;
    private readonly TimeSpan sourceTimeout;

    public GetFxRateHandler(MarketDataDbContext dbContext, IFxRateSource fxRateSource, ILogger<GetFxRateHandler> logger)
        : this(dbContext, fxRateSource, logger, DefaultSourceTimeout)
    {
    }

    // Tests pass a short budget so the timeout path doesn't wait the full 5 s.
    public GetFxRateHandler(
        MarketDataDbContext dbContext, IFxRateSource fxRateSource, ILogger<GetFxRateHandler> logger, TimeSpan sourceTimeout)
    {
        this.dbContext = dbContext;
        this.fxRateSource = fxRateSource;
        this.logger = logger;
        this.sourceTimeout = sourceTimeout;
    }

    public async Task<Result<FxRateResponse>> HandleAsync(string currency, DateOnly date, CancellationToken cancellationToken)
    {
        var code = currency.Trim().ToUpperInvariant();
        var pair = $"{code}{Money.BaseCurrency}";

        var rate = await dbContext.FxRates
            .AsNoTracking()
            .Where(r => r.Pair == pair && r.Date <= date)
            .OrderByDescending(r => r.Date)
            .Select(r => new FxRateResponse(code, r.Date, r.Rate))
            .FirstOrDefaultAsync(cancellationToken);

        if (rate is not null)
        {
            return rate;
        }

        if (code == Money.BaseCurrency)
        {
            return FxRateErrors.NotFound(code, date);
        }

        var currencyRow = await dbContext.Currencies
            .AsNoTracking()
            .Where(c => c.Code == code)
            .Select(c => new { c.HistoryCoveredFrom })
            .FirstOrDefaultAsync(cancellationToken);

        if (currencyRow is null || currencyRow.HistoryCoveredFrom is { } coveredFrom && coveredFrom <= date)
        {
            return FxRateErrors.NotFound(code, date);
        }

        return await ReadThroughAsync(code, date, cancellationToken);
    }

    // Answered straight from the provider and never stored; the position event's backfill fills the history.
    private async Task<Result<FxRateResponse>> ReadThroughAsync(string code, DateOnly date, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(sourceTimeout);

        PriceFetchResult<FxRateQuote> result;
        try
        {
            result = await fxRateSource.FetchHistoryAsync(code, date.AddDays(-ReadThroughWindowDays), date, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("GetFxRate: {Currency} read-through for {Date} exceeded the {Timeout} budget.", code, date, sourceTimeout);
            return FxRateErrors.Unavailable(code);
        }

        switch (result.Outcome)
        {
            case PriceFetchOutcome.Success:
                var latest = result.Values
                    .Where(v => v.Date <= date)
                    .OrderByDescending(v => v.Date)
                    .FirstOrDefault();
                return latest is null
                    ? FxRateErrors.NotFound(code, date)
                    : new FxRateResponse(code, latest.Date, latest.Rate);
            case PriceFetchOutcome.NoData:
                return FxRateErrors.NotFound(code, date);
            default:
                logger.LogWarning("GetFxRate: {Currency} read-through for {Date} failed: {Reason}", code, date, result.ErrorReason);
                return FxRateErrors.Unavailable(code);
        }
    }
}

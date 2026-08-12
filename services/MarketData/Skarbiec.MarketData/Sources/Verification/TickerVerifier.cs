using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.Verification;

/// <summary>
/// Real <see cref="ITickerVerifier"/> — the one request-path exception to ADR-007, contained exactly
/// as ADR-018 describes: a ticker already <see cref="InstrumentVerificationStatus.Verified"/> in
/// <see cref="MarketDataDbContext.Instruments"/> is confirmed from the database with no external call
/// at all; anything else gets exactly one attempt against the matching <see cref="IPriceSource"/>,
/// bounded by <see cref="DefaultTimeout"/> — deliberately short and non-retrying, unlike
/// <c>CoinGeckoPriceSource</c>'s job-time rate-limit retry, which sleeps on <c>Retry-After</c> and
/// would turn a single verification into a multi-second (or longer) stall in a request path.
/// </summary>
public sealed class TickerVerifier : ITickerVerifier
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly MarketDataDbContext _dbContext;
    private readonly IEnumerable<IPriceSource> _priceSources;
    private readonly ILogger<TickerVerifier> _logger;
    private readonly TimeSpan _timeout;

    public TickerVerifier(MarketDataDbContext dbContext, IEnumerable<IPriceSource> priceSources, ILogger<TickerVerifier> logger)
        : this(dbContext, priceSources, logger, DefaultTimeout)
    {
    }

    /// <summary>Test seam: lets <c>TickerVerifierTests</c> prove the request-path timeout budget
    /// (ADR-018) without actually waiting out <see cref="DefaultTimeout"/>, mirroring
    /// <c>CoinGeckoPriceSource</c>'s own delay test seam.</summary>
    public TickerVerifier(MarketDataDbContext dbContext, IEnumerable<IPriceSource> priceSources, ILogger<TickerVerifier> logger, TimeSpan timeout)
    {
        _dbContext = dbContext;
        _priceSources = priceSources;
        _logger = logger;
        _timeout = timeout;
    }

    public async Task<TickerVerificationOutcome> VerifyAsync(PriceSource source, string ticker, CancellationToken cancellationToken)
    {
        var alreadyVerified = await _dbContext.Instruments.AsNoTracking().AnyAsync(
            i => i.Source == source && i.Ticker == ticker && i.VerificationStatus == InstrumentVerificationStatus.Verified,
            cancellationToken);
        if (alreadyVerified)
        {
            return TickerVerificationOutcome.Exists;
        }

        var priceSource = _priceSources.FirstOrDefault(s => s.Source == source);
        if (priceSource is null)
        {
            _logger.LogWarning("TickerVerifier: no IPriceSource registered for {Source}; treating as unreachable.", source);
            return TickerVerificationOutcome.Unreachable;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        // Ephemeral probe, never persisted (ADR-018) — every IPriceSource.FetchLatestAsync
        // implementation keys purely off Instrument.Ticker; Name/QuoteCurrency/AssetClass aren't
        // inspected (see Stooq/CoinGecko/NBP's own FetchLatestAsync).
        var probe = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Source = source,
            QuoteCurrency = string.Empty,
            AssetClass = AssetClass.Other,
        };

        PriceFetchResult<InstrumentQuote> result;
        try
        {
            result = await priceSource.FetchLatestAsync([probe], timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own budget expired (e.g. a rate-limit backoff sleep on the underlying source) — the
            // caller didn't cancel, so this is "unreachable", not a propagated cancellation. Mirrors
            // Portfolio's MarketDataInstrumentLookupClient.CheckAsync (T2.9).
            _logger.LogWarning("TickerVerifier: {Source} verification for '{Ticker}' exceeded the {Timeout} budget.", source, ticker, _timeout);
            return TickerVerificationOutcome.Unreachable;
        }

        return result.Outcome switch
        {
            PriceFetchOutcome.Success => TickerVerificationOutcome.Exists,
            PriceFetchOutcome.NoData => TickerVerificationOutcome.DoesNotExist,
            PriceFetchOutcome.Error => TickerVerificationOutcome.Unreachable,
            _ => TickerVerificationOutcome.Unreachable,
        };
    }
}

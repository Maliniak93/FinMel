using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Securities;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Watchlist;

internal static class WatchlistReader
{
    private const string BaseCurrency = "PLN";

    public static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public static async Task<IReadOnlyList<WatchlistItemResponse>> ToResponsesAsync(
        this IReadOnlyList<WatchlistItem> items,
        PortfolioDbContext dbContext,
        IInstrumentQuoteLookupClient quoteLookup,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var instrumentIds = items.Select(i => i.InstrumentId).Distinct().ToList();

        var holdings = (await (
                from asset in dbContext.Assets.AsNoTracking()
                join account in dbContext.Accounts.AsNoTracking() on asset.AccountId equals (Guid?)account.Id
                where asset.ValuationMode == AssetValuationMode.Market
                    && asset.InstrumentId != null
                    && instrumentIds.Contains(asset.InstrumentId.Value)
                    && !asset.IsArchived
                    && !account.IsArchived
                orderby account.Name, asset.Name
                select new
                {
                    InstrumentId = asset.InstrumentId!.Value,
                    Holding = new WatchlistHoldingResponse
                    {
                        AccountId = account.Id,
                        AccountName = account.Name,
                        AssetId = asset.Id,
                        AssetName = asset.Name,
                        Quantity = asset.Quantity
                    }
                })
            .ToListAsync(cancellationToken))
            .ToLookup(h => h.InstrumentId, h => h.Holding);

        var quotes = await quoteLookup.GetQuotesAsync(instrumentIds, WarsawCalendar.Today(timeProvider), cancellationToken);

        return [.. items.Select(item => ToResponse(item, [.. holdings[item.InstrumentId]], quotes))];
    }

    private static WatchlistItemResponse ToResponse(
        WatchlistItem item, IReadOnlyList<WatchlistHoldingResponse> holdings, InstrumentQuoteLookupResult quotes)
    {
        var quote = quotes.Instruments?.GetValueOrDefault(item.InstrumentId);

        SecurityPriceUnavailableReason? reason = quotes.Status == InstrumentQuoteLookupStatus.Unavailable
            ? SecurityPriceUnavailableReason.MarketDataUnavailable
            : quote?.LastPrice is null ? SecurityPriceUnavailableReason.NoQuote : null;

        var response = new WatchlistItemResponse
        {
            Id = item.Id,
            InstrumentId = item.InstrumentId,
            AssetClass = item.AssetClass,
            Ticker = item.Ticker,
            Name = item.Name,
            Exchange = item.Exchange,
            QuoteCurrency = item.QuoteCurrency,
            Note = item.Note,
            TargetPrice = item.TargetPrice,
            LastPrice = null,
            LastPriceDate = null,
            PreviousClose = null,
            DailyChange = null,
            DailyChangePercent = null,
            LastPricePln = null,
            TargetDistancePercent = null,
            AtOrBelowTarget = false,
            PriceUnavailableReason = reason,
            Holdings = holdings,
            HeldQuantity = holdings.Sum(h => h.Quantity)
        };

        if (reason is not null)
        {
            return response;
        }

        var lastPrice = quote!.LastPrice!.Value;
        var previousClose = quote.PreviousClose;
        var dailyChange = previousClose is { } previous ? lastPrice - previous : (decimal?)null;

        decimal? rate = null;
        if (item.QuoteCurrency == BaseCurrency)
        {
            rate = 1m;
        }
        else if (quotes.Rates?.TryGetValue(item.QuoteCurrency, out var found) == true)
        {
            rate = found;
        }

        return response with
        {
            LastPrice = lastPrice,
            LastPriceDate = quote.LastPriceDate,
            PreviousClose = previousClose,
            DailyChange = dailyChange,
            DailyChangePercent = dailyChange is { } change && previousClose is > 0
                ? Round(change / previousClose.Value * 100)
                : null,
            LastPricePln = rate is { } r ? Round(lastPrice * r) : null,
            TargetDistancePercent = item.TargetPrice is { } target
                ? Round((lastPrice - target) / target * 100)
                : null,
            AtOrBelowTarget = item.TargetPrice is { } limit && lastPrice <= limit,
            PriceUnavailableReason = rate is null ? SecurityPriceUnavailableReason.FxRateMissing : null
        };
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

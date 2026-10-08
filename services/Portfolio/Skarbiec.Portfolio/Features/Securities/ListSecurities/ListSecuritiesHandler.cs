using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Securities.ListSecurities;

public sealed class ListSecuritiesHandler(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    private const string BaseCurrency = "PLN";

    public async Task<Result<SecuritiesResponse>> HandleAsync(AssetClass assetClass, CancellationToken cancellationToken)
    {
        if (assetClass is not (AssetClass.Stock or AssetClass.Etf))
        {
            return SecuritiesErrors.UnsupportedAssetClass;
        }

        var assets = await (
                from asset in dbContext.Assets.AsNoTracking()
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                where asset.AssetClass == assetClass
                    && asset.ValuationMode == AssetValuationMode.Market
                    && asset.InstrumentId != null
                    && !asset.IsArchived
                    && !portfolio.IsArchived
                orderby portfolio.Name, asset.Name
                select new
                {
                    AssetId = asset.Id,
                    PortfolioId = portfolio.Id,
                    PortfolioName = portfolio.Name,
                    asset.Name,
                    InstrumentId = asset.InstrumentId!.Value,
                    asset.Currency
                })
            .ToListAsync(cancellationToken);

        var assetIds = assets.Select(a => a.AssetId).ToList();
        var transactions = (await dbContext.Transactions
                .AsNoTracking()
                .Where(t => assetIds.Contains(t.AssetId))
                .ToListAsync(cancellationToken))
            .ToLookup(t => t.AssetId);

        var instrumentIds = assets.Select(a => a.InstrumentId).Distinct().ToList();
        var quotes = instrumentIds.Count == 0
            ? InstrumentQuoteLookupResult.Found(new Dictionary<Guid, InstrumentQuote>(), new Dictionary<string, decimal>())
            : await quoteLookup.GetQuotesAsync(instrumentIds, WarsawCalendar.Today(timeProvider), cancellationToken);

        var holdings = assets
            .Select(a => ToHolding(
                a.AssetId, a.PortfolioId, a.PortfolioName, a.Name, a.InstrumentId, a.Currency,
                SecurityCostBasis.Compute(transactions[a.AssetId]), quotes))
            .ToList();

        return new SecuritiesResponse
        {
            Holdings = holdings,
            Totals = new SecuritiesTotalsResponse
            {
                ValuePln = holdings.Sum(h => h.ValuePln ?? 0m),
                CostPln = holdings.Sum(h => h.CostPln ?? 0m),
                UnrealizedPlPln = holdings.Sum(h => h.UnrealizedPlPln ?? 0m)
            }
        };
    }

    private static SecurityHoldingResponse ToHolding(
        Guid assetId,
        Guid portfolioId,
        string portfolioName,
        string name,
        Guid instrumentId,
        string currency,
        SecurityCostBasisResult basis,
        InstrumentQuoteLookupResult quotes)
    {
        var costPln = basis.CostPln is { } cost ? Round(cost) : (decimal?)null;
        var instrument = quotes.Instruments?.GetValueOrDefault(instrumentId);

        SecurityPriceUnavailableReason? reason = quotes.Status == InstrumentQuoteLookupStatus.Unavailable
            ? SecurityPriceUnavailableReason.MarketDataUnavailable
            : instrument?.LastPrice is null ? SecurityPriceUnavailableReason.NoQuote : null;

        decimal? rate = null;
        if (reason is null)
        {
            decimal found = 1m;
            var hasRate = instrument!.QuoteCurrency == BaseCurrency || (quotes.Rates?.TryGetValue(instrument.QuoteCurrency, out found) ?? false);
            rate = hasRate ? found : null;
            if (rate is null)
            {
                reason = SecurityPriceUnavailableReason.FxRateMissing;
            }
        }

        var holding = new SecurityHoldingResponse
        {
            AssetId = assetId,
            PortfolioId = portfolioId,
            PortfolioName = portfolioName,
            Name = name,
            InstrumentId = instrumentId,
            Ticker = instrument?.Ticker,
            Exchange = instrument?.Exchange,
            Currency = currency,
            Quantity = basis.Quantity,
            AverageBuyPrice = basis.AverageBuyPrice,
            CostPln = costPln,
            LastPrice = null,
            LastPriceDate = null,
            ValuePln = null,
            UnrealizedPl = null,
            UnrealizedPlPercent = null,
            UnrealizedPlPln = null,
            PriceUnavailableReason = reason
        };

        if (reason is not null)
        {
            return holding;
        }

        var lastPrice = instrument!.LastPrice!.Value;
        var valuePln = Round(basis.Quantity * lastPrice * rate!.Value);
        var unrealizedPl = Round(basis.Quantity * lastPrice - basis.CostQuote);

        return holding with
        {
            LastPrice = lastPrice,
            LastPriceDate = instrument.LastPriceDate,
            ValuePln = valuePln,
            UnrealizedPl = unrealizedPl,
            UnrealizedPlPercent = basis.CostQuote == 0 ? null : Round(unrealizedPl / basis.CostQuote * 100),
            UnrealizedPlPln = costPln is null ? null : valuePln - costPln
        };
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

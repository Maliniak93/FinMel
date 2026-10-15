using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.CashPlaces;

internal static class CashPlaceReads
{
    public static async Task<CashPlacesResponse> ReadAsync(
        this PortfolioDbContext dbContext,
        IInstrumentQuoteLookupClient quoteLookup,
        TimeProvider timeProvider,
        Guid? placeId,
        CancellationToken cancellationToken)
    {
        var places = await dbContext.CashPlaces
            .AsNoTracking()
            .Where(p => placeId == null || p.Id == placeId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var placeIds = places.Select(p => p.Id).ToList();
        var balances = (await dbContext.Assets
                .AsNoTracking()
                .Where(a => a.CashPlaceId != null && placeIds.Contains(a.CashPlaceId.Value))
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.CashPlaceId!.Value);

        // A zero balance is worth 0 at any rate, so only held foreign currencies ask MarketData.
        var foreign = balances
            .SelectMany(g => g)
            .Where(a => a.Currency != CashPlaceBalances.BaseCurrency && a.Quantity != 0m)
            .Select(a => a.Currency)
            .Distinct()
            .ToList();

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var fxUnavailable = false;
        if (foreign.Count > 0)
        {
            var lookup = await quoteLookup.GetRatesAsync(foreign, WarsawCalendar.Today(timeProvider), cancellationToken);
            if (lookup.Status == InstrumentQuoteLookupStatus.Found)
            {
                foreach (var (currency, rate) in lookup.Rates!)
                {
                    rates[currency] = rate;
                }
            }

            fxUnavailable = foreign.Any(c => !rates.ContainsKey(c));
        }

        decimal? ValueOf(Asset balance) =>
            balance.Quantity == 0m ? 0m
            : balance.Currency == CashPlaceBalances.BaseCurrency ? Round(balance.Quantity)
            : rates.TryGetValue(balance.Currency, out var rate) ? Round(balance.Quantity * rate)
            : null;

        var responses = places
            .Select(place =>
            {
                var response = place.ToResponse(balances[place.Id]);
                var values = response.Balances
                    .Select(b => b with { ValuePln = ValueOf(balances[place.Id].Single(a => a.Id == b.CashAssetId)) })
                    .ToList();

                return response with { Balances = values, ValuePln = Sum(values.Select(v => v.ValuePln)) };
            })
            .ToList();

        var totals = balances
            .SelectMany(g => g)
            .GroupBy(a => a.Currency)
            .Select(g => new CashTotalResponse { Currency = g.Key, Balance = g.Sum(a => a.Quantity) })
            .OrderBy(t => t.Currency == CashPlaceBalances.BaseCurrency ? 0 : 1)
            .ThenBy(t => t.Currency, StringComparer.Ordinal)
            .ToList();

        return new CashPlacesResponse
        {
            Places = responses,
            Totals = totals,
            TotalValuePln = Sum(responses.Select(p => p.ValuePln)),
            FxUnavailable = fxUnavailable
        };
    }

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var list = values.ToList();

        return list.Any(v => v is null) ? null : list.Sum(v => v!.Value);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

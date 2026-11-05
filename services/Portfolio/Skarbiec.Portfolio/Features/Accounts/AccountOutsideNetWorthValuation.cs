using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Accounts;

internal static class AccountOutsideNetWorthValuation
{
    // One latest-rate call for the distinct foreign currencies of the flagged accounts; a missing rate leaves null (ADR-036).
    public static async Task<Dictionary<Guid, decimal?>> ValueAsync(
        IInstrumentQuoteLookupClient quoteLookup,
        TimeProvider timeProvider,
        IEnumerable<(Account Account, Asset Cash)> accounts,
        CancellationToken cancellationToken)
    {
        var flagged = accounts.Where(a => a.Account.OutsideNetWorth).ToList();

        var foreign = flagged
            .Where(a => a.Cash.Currency != AccountCash.Currency && a.Cash.Quantity != 0m)
            .Select(a => a.Cash.Currency)
            .Distinct()
            .ToList();

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
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
        }

        return flagged.ToDictionary(a => a.Account.Id, a => ValueOf(a.Cash, rates));
    }

    private static decimal? ValueOf(Asset cash, Dictionary<string, decimal> rates)
        => cash.Quantity == 0m ? 0m
            : cash.Currency == AccountCash.Currency ? Round(cash.Quantity)
            : rates.TryGetValue(cash.Currency, out var rate) ? Round(cash.Quantity * rate)
            : null;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

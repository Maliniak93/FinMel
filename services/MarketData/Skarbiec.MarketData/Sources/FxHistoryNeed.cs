using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

public sealed record FxHistoryOptions(int MinHistoryDays = 365, int HistoryMarginDays = 14);

public static class FxHistoryNeed
{
    public const string BaseCurrency = "PLN";

    public static async Task<DateOnly> NeedDateAsync(
        MarketDataDbContext db, DateOnly today, FxHistoryOptions? options, CancellationToken cancellationToken)
    {
        options ??= new FxHistoryOptions();
        var oldest = await db.AssetInstrumentLinks
            .Where(l => !l.IsRemoved)
            .MinAsync(l => l.FirstTransactionDate, cancellationToken);

        var horizon = today.AddDays(-options.MinHistoryDays);
        var basis = oldest is { } first && first < horizon ? first : horizon;
        return basis.AddDays(-options.HistoryMarginDays);
    }

    public static IQueryable<Currency> Lagging(MarketDataDbContext db, DateOnly needDate) =>
        db.Currencies.Where(c => c.Code != BaseCurrency && (c.HistoryCoveredFrom == null || c.HistoryCoveredFrom > needDate));

    public static async Task<bool> AnyLaggingAsync(
        MarketDataDbContext db, DateOnly today, FxHistoryOptions? options, CancellationToken cancellationToken)
    {
        var needDate = await NeedDateAsync(db, today, options, cancellationToken);
        return await Lagging(db, needDate).AnyAsync(cancellationToken);
    }
}

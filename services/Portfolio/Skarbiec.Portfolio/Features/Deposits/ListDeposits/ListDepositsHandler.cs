using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.ListDeposits;

public sealed class ListDepositsHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<DepositResponse>> HandleAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        var rows = await (
                from terms in dbContext.TermDeposits.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                where accountId == null || terms.SourceAccountId == accountId || terms.ReturnAccountId == accountId
                orderby terms.MaturityDate, asset.Name
                select new { Terms = terms, Asset = asset })
            .ToListAsync(cancellationToken);

        var today = WarsawCalendar.Today(timeProvider);
        var accountNames = await dbContext.LoadAccountNamesAsync(rows.Select(r => r.Terms), cancellationToken);
        var payoutDates = await dbContext.LoadPayoutDatesAsync(rows.Select(r => r.Asset.Id).ToList(), cancellationToken);

        return rows
            .Select(r => r.Terms.ToResponse(
                r.Asset,
                today,
                accountNames,
                payoutDates.TryGetValue(r.Asset.Id, out var paidOutOn) ? paidOutOn : null))
            .ToList();
    }
}

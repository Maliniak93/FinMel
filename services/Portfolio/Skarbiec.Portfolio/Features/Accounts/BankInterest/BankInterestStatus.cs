using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest;

internal sealed record BankInterestState(
    BankAccountTerms Terms,
    BankInterestDuePeriod? Due,
    BankInterestSettlement? Latest,
    decimal CreditedNet)
{
    public BankTermsResponse ToResponse() => new()
    {
        Variant = Terms.Variant,
        BankName = Terms.BankName,
        AnnualInterestRatePercent = Terms.AnnualInterestRatePercent,
        InterestDue = Due is not null,
        DuePeriodCount = Due?.DuePeriodCount ?? 0,
        LastSettlement = Latest?.ToResponse(),
        InterestCreditedNet = CreditedNet
    };
}

internal static class BankInterestStatus
{
    public static BankInterestSettlementResponse ToResponse(this BankInterestSettlement settlement) => new()
    {
        SettlementId = settlement.Id,
        PeriodStart = settlement.PeriodStart,
        PeriodEnd = settlement.PeriodEnd,
        GrossInterest = settlement.GrossInterest,
        Tax = settlement.Tax,
        NetInterest = settlement.GrossInterest - settlement.Tax
    };

    public static BankTermsResponse ToNewResponse(this BankAccountTerms terms) =>
        new BankInterestState(terms, null, null, 0m).ToResponse();

    public static async Task<IReadOnlyDictionary<Guid, BankInterestState>> LoadBankInterestAsync(
        this PortfolioDbContext dbContext,
        IReadOnlyCollection<Account> accounts,
        IReadOnlyDictionary<Guid, Asset> moneyByAccount,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var bankAccountIds = accounts.Where(a => a.Type == AccountType.Bank).Select(a => a.Id).ToList();
        if (bankAccountIds.Count == 0)
        {
            return new Dictionary<Guid, BankInterestState>();
        }

        var assetIds = bankAccountIds.Select(id => moneyByAccount[id].Id).ToList();

        var terms = await dbContext.BankAccountTerms
            .AsNoTracking()
            .Where(t => bankAccountIds.Contains(t.AccountId))
            .ToListAsync(cancellationToken);

        var transactions = (await dbContext.Transactions
                .AsNoTracking()
                .Where(t => assetIds.Contains(t.AssetId))
                .ToListAsync(cancellationToken))
            .ToLookup(t => t.AssetId);

        // At most one settlement per account per month, so loading them all stays small.
        var settlements = (await dbContext.BankInterestSettlements
                .AsNoTracking()
                .Where(s => assetIds.Contains(s.AssetId))
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.AssetId);

        return terms.ToDictionary(
            t => t.AccountId,
            t =>
            {
                var assetId = moneyByAccount[t.AccountId].Id;
                var latest = settlements[assetId].MaxBy(s => s.PeriodEnd);
                var due = BankInterestMath.NextDuePeriod(
                    [.. transactions[assetId].Select(BankCashFlow.From)],
                    t.AnnualInterestRatePercent,
                    latest?.PeriodEnd,
                    today);

                return new BankInterestState(t, due, latest, settlements[assetId].Sum(s => s.GrossInterest - s.Tax));
            });
    }

    public static async Task<BankInterestState?> LoadBankInterestAsync(
        this PortfolioDbContext dbContext, Account account, Asset money, DateOnly today, CancellationToken cancellationToken)
        => (await dbContext.LoadBankInterestAsync(
            [account], new Dictionary<Guid, Asset> { [account.Id] = money }, today, cancellationToken)).GetValueOrDefault(account.Id);
}

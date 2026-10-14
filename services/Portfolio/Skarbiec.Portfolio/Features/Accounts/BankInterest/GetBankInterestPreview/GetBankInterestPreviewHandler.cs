using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest.GetBankInterestPreview;

public sealed class GetBankInterestPreviewHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<BankInterestPreviewResponse>> HandleAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var found = await dbContext.FindOpenBankAccountAsync(accountId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var state = await dbContext.LoadBankInterestAsync(
            found.Value.Account, found.Value.Money, WarsawCalendar.Today(timeProvider), cancellationToken);
        if (state?.Due is not { } due)
        {
            return BankInterestErrors.NotDue;
        }

        return new BankInterestPreviewResponse
        {
            PeriodStart = due.PeriodStart,
            PeriodEnd = due.PeriodEnd,
            AnnualInterestRatePercent = state.Terms.AnnualInterestRatePercent,
            AverageDailyBalance = due.Accrual.AverageDailyBalance,
            GrossInterest = due.Accrual.GrossInterest,
            Tax = due.Accrual.Tax,
            NetInterest = due.Accrual.NetInterest,
            DuePeriodCount = due.DuePeriodCount
        };
    }
}

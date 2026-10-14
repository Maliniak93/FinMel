using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.UpdateAccount;

public sealed class UpdateAccountHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<AccountResponse>> HandleAsync(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        BankAccountTerms? terms = null;
        if (account.Type == AccountType.Bank)
        {
            if (request.Variant is null)
            {
                return AccountErrors.BankVariantRequired;
            }

            terms = await dbContext.BankAccountTerms.FirstAsync(t => t.AccountId == id, cancellationToken);
        }
        else if (request.Variant is not null || request.BankName is not null || request.AnnualInterestRatePercent is not null)
        {
            return AccountErrors.BankFieldsNotAllowed;
        }

        if (!string.Equals(account.Name, request.Name, StringComparison.Ordinal))
        {
            var nameTaken = await dbContext.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id != id && a.Name == request.Name, cancellationToken);

            if (nameTaken)
            {
                return AccountErrors.DuplicateName(request.Name);
            }
        }

        var cash = await dbContext.LoadAccountCashAsync(id, cancellationToken);

        // The cash carries the account's name; no event, since AssetPositionChanged holds no name.
        account.Name = request.Name;
        cash.Name = request.Name;

        if (terms is not null)
        {
            terms.Variant = request.Variant!.Value;
            terms.BankName = request.BankName;
            terms.AnnualInterestRatePercent = request.AnnualInterestRatePercent ?? 0m;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await dbContext.ToResponseAsync(account, timeProvider, cancellationToken);
    }
}

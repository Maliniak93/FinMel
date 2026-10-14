using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.BankInterest;

namespace Skarbiec.Portfolio.Features.Accounts.CreateAccount;

public sealed class CreateAccountHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result<AccountResponse>> HandleAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var currency = request.Currency ?? AccountCash.Currency;

        if (request.Type == AccountType.Bank)
        {
            if (request.Variant is null)
            {
                return AccountErrors.BankVariantRequired;
            }

            if (request.Wrapper != TaxWrapper.Regular)
            {
                return AccountErrors.WrapperNotAllowed;
            }
        }
        else if (request.Variant is not null
            || request.BankName is not null
            || request.AnnualInterestRatePercent is not null
            || currency != AccountCash.Currency)
        {
            return AccountErrors.BankFieldsNotAllowed;
        }

        var nameTaken = await dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Name == request.Name, cancellationToken);

        if (nameTaken)
        {
            return AccountErrors.DuplicateName(request.Name);
        }

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Type = request.Type,
            Wrapper = request.Wrapper,
            Currency = currency
        };

        var money = new Asset
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            AssetClass = AccountCash.MoneyClassOf(request.Type),
            Name = account.Name,
            Currency = currency,
            ValuationMode = AssetValuationMode.CurrencyValued
        };

        dbContext.Accounts.Add(account);
        dbContext.Assets.Add(money);

        BankAccountTerms? terms = null;
        if (request.Variant is { } variant)
        {
            terms = new BankAccountTerms
            {
                AccountId = account.Id,
                Variant = variant,
                BankName = request.BankName,
                AnnualInterestRatePercent = request.AnnualInterestRatePercent ?? 0m
            };
            dbContext.BankAccountTerms.Add(terms);
        }

        // The account, its money asset and the asset's position event commit together.
        await positionEventPublisher.PublishCreatedAsync(money, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return account.ToResponse(money) with { Bank = terms?.ToNewResponse() };
    }
}

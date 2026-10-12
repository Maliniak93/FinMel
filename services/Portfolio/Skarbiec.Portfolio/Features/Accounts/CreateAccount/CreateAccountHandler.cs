using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.CreateAccount;

public sealed class CreateAccountHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result<AccountResponse>> HandleAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
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
            Wrapper = request.Wrapper
        };

        var cash = new Asset
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            AssetClass = AssetClass.Cash,
            Name = account.Name,
            Currency = AccountCash.Currency,
            ValuationMode = AssetValuationMode.CurrencyValued
        };

        dbContext.Accounts.Add(account);
        dbContext.Assets.Add(cash);

        // The account, its cash and the cash's position event commit together.
        await positionEventPublisher.PublishCreatedAsync(cash, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return account.ToResponse(cash);
    }
}

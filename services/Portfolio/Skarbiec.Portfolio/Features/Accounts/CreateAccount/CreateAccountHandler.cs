using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.CreateAccount;

public sealed class CreateAccountHandler(PortfolioDbContext dbContext)
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

        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);

        return account.ToResponse();
    }
}

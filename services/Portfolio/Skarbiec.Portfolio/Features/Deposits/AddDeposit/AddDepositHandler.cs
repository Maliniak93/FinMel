using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.AssetGroups;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Deposits.AddDeposit;

public sealed class AddDepositHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(
        AddDepositRequest request, CancellationToken cancellationToken)
    {
        if (await dbContext.CheckAsync(request.GroupId, cancellationToken) is { } groupError)
        {
            return groupError;
        }

        var source = await dbContext.ResolveSourceAsync(request.SourceAccountId, cancellationToken);
        if (source.IsFailure)
        {
            return source.Error;
        }

        var sourceAccount = source.Value;
        var returnAccount = sourceAccount;
        if (request.ReturnAccountId is { } returnAccountId && returnAccountId != sourceAccount.Id)
        {
            var resolved = await dbContext.ResolveReturnAsync(returnAccountId, sourceAccount.Currency, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }

            returnAccount = resolved.Value;
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            GroupId = request.GroupId,
            AssetClass = AssetClass.Deposit,
            Name = request.Name,
            Currency = sourceAccount.Currency,
            ValuationMode = AssetValuationMode.CurrencyValued,
        };

        var bankMoney = await dbContext.LoadAccountCashAsync(sourceAccount.Id, cancellationToken);
        var (outLeg, opening) = TransferLegs.Create(bankMoney.Id, asset.Id, request.Principal, request.StartDate);

        var bankHistory = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == bankMoney.Id)
            .ToListAsync(cancellationToken);

        var bankQuantity = TransactionQuantityCalculator.Recompute(
            [.. bankHistory, outLeg], _ => TransferErrors.InsufficientFunds);
        if (bankQuantity.IsFailure)
        {
            return bankQuantity.Error;
        }

        // The opening In leg is the only way the principal reaches Asset.Quantity.
        var recomputed = TransactionQuantityCalculator.Recompute([opening]);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        // Last check before staging: MarketData being down leaves nothing half-created; both legs share the one rate.
        var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.StartDate, cancellationToken);
        if (fxRateToPln.IsFailure)
        {
            return fxRateToPln.Error;
        }

        opening.FxRateToPln = fxRateToPln.Value;
        outLeg.FxRateToPln = fxRateToPln.Value;
        asset.Quantity = recomputed.Value;
        bankMoney.Quantity = bankQuantity.Value;

        var terms = new TermDeposit
        {
            AssetId = asset.Id,
            BankName = request.BankName,
            SourceAccountId = sourceAccount.Id,
            ReturnAccountId = returnAccount.Id,
            Principal = request.Principal,
            StartDate = request.StartDate,
            TermLength = request.TermLength,
            TermUnit = request.TermUnit,
            MaturityDate = DepositInterestMath.MaturityDate(request.StartDate, request.TermLength, request.TermUnit),
            AnnualInterestRatePercent = request.AnnualInterestRatePercent,
            Capitalization = request.Capitalization,
            EarlyBreakInterestLossPercent = request.EarlyBreakInterestLossPercent
        };

        dbContext.Assets.Add(asset);
        dbContext.Transactions.Add(opening);
        dbContext.Transactions.Add(outLeg);
        dbContext.TermDeposits.Add(terms);

        // The bank account publishes in the same save as both legs and the deposit's own event.
        await positionEventPublisher.PublishCreatedAsync(asset, cancellationToken);
        await positionEventPublisher.PublishChangedAsync(bankMoney, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A write racing on the bank account's money moved its xmin: nothing is saved, the user retries.
            return TransactionErrors.ConcurrentModification();
        }

        var accountNames = new Dictionary<Guid, string>
        {
            [sourceAccount.Id] = sourceAccount.Name,
            [returnAccount.Id] = returnAccount.Name
        };

        return terms.ToResponse(asset, WarsawCalendar.Today(timeProvider), accountNames, paidOutOn: null);
    }
}

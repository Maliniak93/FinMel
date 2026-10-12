using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds.AddBond;

public sealed class AddBondHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    TimeProvider timeProvider)
{
    private const string Currency = "PLN";

    public async Task<Result<BondResponse>> HandleAsync(
        AddBondRequest request, CancellationToken cancellationToken)
    {
        var resolved = await dbContext.ResolveAsync(AssetClass.Bond, request.AccountId, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var account = resolved.Value!;

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            AssetClass = AssetClass.Bond,
            Name = request.Name,
            Currency = Currency,
            ValuationMode = AssetValuationMode.CurrencyValued,
        };

        var cost = request.BondCount * request.PurchasePricePerBond;

        // The system-managed opening transaction, the In leg of the account cash's transfer, is the only way the purchase cost reaches Asset.Quantity.
        var cash = await dbContext.LoadAccountCashAsync(account.Id, cancellationToken);
        var (cashLeg, opening) = TransferLegs.Create(cash.Id, asset.Id, cost, request.PurchaseDate);

        var cashQuantity = await dbContext.RecomputeWithAsync(
            cash.Id, [cashLeg], cancellationToken, _ => TransferErrors.InsufficientFunds);
        if (cashQuantity.IsFailure)
        {
            return cashQuantity.Error;
        }

        var recomputed = TransactionQuantityCalculator.Recompute([opening]);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        // Both sides are PLN: the frozen PLN rate is 1 by definition, so MarketData is not asked.
        opening.FxRateToPln = 1m;
        cashLeg.FxRateToPln = 1m;
        asset.Quantity = recomputed.Value;
        cash.Quantity = cashQuantity.Value;

        var terms = new TreasuryBond
        {
            AssetId = asset.Id,
            SeriesCode = request.SeriesCode,
            Type = request.Type,
            PurchaseDate = request.PurchaseDate,
            BondCount = request.BondCount,
            PurchasePricePerBond = request.PurchasePricePerBond,
            FirstPeriodRatePercent = request.FirstPeriodRatePercent,
            MarginPercent = request.MarginPercent,
            EarlyRedemptionFeePerBond = request.EarlyRedemptionFeePerBond,
            MaturityDate = BondSchedule.MaturityDate(request.Type, request.PurchaseDate)
        };

        dbContext.Assets.Add(asset);
        dbContext.Transactions.AddRange(opening, cashLeg);
        dbContext.TreasuryBonds.Add(terms);

        await positionEventPublisher.PublishCreatedAsync(asset, cancellationToken);

        // The account cash publishes in the same save as both legs and the bond's own event.
        await positionEventPublisher.PublishChangedAsync(cash, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A write racing on the account cash moved its xmin: nothing is saved, the user retries.
            return TransactionErrors.ConcurrentModification();
        }

        return terms.ToResponse(asset, account, WarsawCalendar.Today(timeProvider), [], [], null);
    }
}

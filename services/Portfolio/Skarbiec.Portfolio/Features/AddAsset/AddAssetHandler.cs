using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed class AddAssetHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IInstrumentLookupClient instrumentLookupClient,
    IFxRateLookupClient fxRateLookupClient)
{
    public async Task<Result<AssetResponse>> HandleAsync(AddAssetRequest request, CancellationToken cancellationToken)
    {
        // A Deposit-class asset is a term deposit: it carries terms, so only AddDeposit creates one.
        if (request.AssetClass == AssetClass.Deposit)
        {
            return DepositErrors.UseDepositEndpoints;
        }

        // A Bond-class asset is a treasury bond holding, created only by AddBond.
        if (request.AssetClass == AssetClass.Bond)
        {
            return BondErrors.UseBondEndpoints;
        }

        // Likewise only CreateAccount creates a BankAccount-class asset.
        if (request.AssetClass == AssetClass.BankAccount)
        {
            return AccountErrors.UseAccountEndpoints;
        }

        // And only AddMetal creates a PreciousMetal-class asset, with its fixed instrument and fine weight.
        if (request.AssetClass == AssetClass.PreciousMetal)
        {
            return MetalErrors.UseMetalEndpoints;
        }

        // Before the instrument and FX lookups: a disallowed opening type calls nothing and creates nothing.
        if (request.InitialTransaction is { } initial && !AssetTransactionTypes.IsAllowed(request.AssetClass, initial.Type))
        {
            return TransactionErrors.TypeNotAllowedForClass(initial.Type, request.AssetClass);
        }

        if (request.InitialTransaction is { } opening)
        {
            if (opening.CashAssetId is not null && BrokerageTrade.Applies(request.AssetClass))
            {
                return TransferErrors.CashLinkNotAllowed;
            }

            if (BrokerageTrade.FeeError(request.AssetClass, opening.Type, opening.FeeAmount) is { } feeError)
            {
                return feeError;
            }

            if (BrokerageTrade.AmountPlnError(
                request.AssetClass, request.Currency, opening.Type, opening.Quantity, opening.UnitPrice, opening.AmountPln) is { } amountPlnError)
            {
                return amountPlnError;
            }
        }

        var account = await dbContext.ResolveAsync(request.AssetClass, request.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return account.Error;
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            AccountId = account.Value?.Id,
            AssetClass = request.AssetClass,
            Name = request.Name,
            Currency = request.Currency,
            // Currency-valued is the fallthrough: AddAssetRequest.Validate already allowed it for this class.
            ValuationMode = AssetValuationMode.CurrencyValued,
        };

        if (request.InstrumentId is { } instrumentId)
        {
            var lookup = await instrumentLookupClient.CheckAsync(instrumentId, cancellationToken);
            if (lookup.Status == InstrumentLookupStatus.NotFound)
            {
                return AssetErrors.InstrumentNotFound(instrumentId);
            }

            if (lookup.Status == InstrumentLookupStatus.Unavailable)
            {
                return AssetErrors.MarketDataUnavailable;
            }

            // A mismatched instrument would value the asset with another class's or currency's prices.
            if (lookup.AssetClass != request.AssetClass)
            {
                return AssetErrors.InstrumentAssetClassMismatch(instrumentId, lookup.AssetClass, request.AssetClass);
            }

            if (!string.Equals(lookup.QuoteCurrency, request.Currency, StringComparison.Ordinal))
            {
                return AssetErrors.InstrumentCurrencyMismatch(instrumentId, lookup.QuoteCurrency, request.Currency);
            }

            asset.InstrumentId = instrumentId;
            asset.ValuationMode = AssetValuationMode.Market;
        }

        // Fully validated before anything is staged, so a failure leaves no half-created asset.
        Transaction? initialTransaction = null;
        BrokerageLegs? brokerage = null;
        if (request.InitialTransaction is { } transactionRequest)
        {
            var unitPrice = Money.Create(transactionRequest.UnitPrice, asset.Currency);
            if (unitPrice.IsFailure)
            {
                return unitPrice.Error;
            }

            initialTransaction = new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                Type = transactionRequest.Type,
                Quantity = transactionRequest.Quantity,
                UnitPriceAmount = BrokerageTrade.Applies(asset.AssetClass)
                    ? BrokerageTrade.SecurityUnitPrice(transactionRequest.Type, unitPrice.Value.Amount)
                    : unitPrice.Value.Amount,
                FeeAmount = transactionRequest.FeeAmount,
                AmountPln = transactionRequest.AmountPln,
                Date = transactionRequest.Date
            };

            // A new asset has no history, so recomputing over this one transaction applies the same oversell check.
            var recomputed = TransactionQuantityCalculator.Recompute([initialTransaction]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            // Resolved after every other check and before staging, so MarketData being down leaves no half-created asset.
            if (BrokerageTrade.Applies(asset.AssetClass))
            {
                var planned = await dbContext.PlanAsync(fxRateLookupClient, asset, initialTransaction, cancellationToken);
                if (planned.IsFailure)
                {
                    return planned.Error;
                }

                brokerage = planned.Value;
            }
            else
            {
                var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(
                    asset.Currency, transactionRequest.Date, cancellationToken);
                if (fxRateToPln.IsFailure)
                {
                    return fxRateToPln.Error;
                }

                initialTransaction.FxRateToPln = fxRateToPln.Value;
            }

            asset.Quantity = recomputed.Value;
        }

        dbContext.Assets.Add(asset);

        if (initialTransaction is not null)
        {
            dbContext.Transactions.Add(initialTransaction);
        }

        // The initial transaction is already folded into asset.Quantity, so a consumer sees the real opening state.
        await positionEventPublisher.PublishCreatedAsync(asset, cancellationToken);

        if (brokerage is not null)
        {
            brokerage.Cash.Quantity = brokerage.CashQuantity;
            dbContext.Transactions.Add(brokerage.CashLeg);
            await positionEventPublisher.PublishChangedAsync(brokerage.Cash, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToResponse(initialTransaction is null ? 0 : 1);
    }
}

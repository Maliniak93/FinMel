using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Features.SavingsAccounts;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed class AddAssetHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IInstrumentLookupClient instrumentLookupClient,
    IFxRateLookupClient fxRateLookupClient)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, AddAssetRequest request, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(portfolioId);
        }

        if (portfolio.IsArchived)
        {
            return PortfolioErrors.Archived(portfolioId);
        }

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

        // Likewise only AddSavingsAccount creates a Savings-class asset.
        if (request.AssetClass == AssetClass.Savings)
        {
            return SavingsAccountErrors.UseSavingsAccountEndpoints;
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

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
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
        else if (request.ManualValue is { } manualAmount)
        {
            var manualValue = Money.Create(manualAmount, request.Currency);
            if (manualValue.IsFailure)
            {
                return manualValue.Error;
            }

            asset.ManualValueAmount = manualValue.Value.Amount;
            asset.ManualValueDate = request.ManualValueDate;
            asset.ValuationMode = AssetValuationMode.Manual;
        }

        // Fully validated before anything is staged, so a failure leaves no half-created asset.
        Transaction? initialTransaction = null;
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
                UnitPriceAmount = unitPrice.Value.Amount,
                Date = transactionRequest.Date
            };

            // A new asset has no history, so recomputing over this one transaction applies the same oversell check.
            var recomputed = TransactionQuantityCalculator.Recompute([initialTransaction]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            // Resolved after every other check and before staging, so MarketData being down leaves no half-created asset.
            var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(
                asset.Currency, transactionRequest.Date, cancellationToken);
            if (fxRateToPln.IsFailure)
            {
                return fxRateToPln.Error;
            }

            initialTransaction.FxRateToPln = fxRateToPln.Value;
            asset.Quantity = recomputed.Value;
        }

        dbContext.Assets.Add(asset);

        if (initialTransaction is not null)
        {
            dbContext.Transactions.Add(initialTransaction);
        }

        // The initial transaction is already folded into asset.Quantity, so a consumer sees the real opening state.
        await positionEventPublisher.PublishCreatedAsync(asset, portfolio, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToResponse(initialTransaction is null ? 0 : 1);
    }
}

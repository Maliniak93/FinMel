using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Features.SavingsAccounts;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.UpdateAsset;

public sealed class UpdateAssetHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, IInstrumentLookupClient instrumentLookupClient)
{
    public async Task<Result<AssetResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, UpdateAssetRequest request, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        if (await dbContext.ReadOnlyErrorAsync(asset, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        // A term deposit is edited only through UpdateDeposit, so no asset becomes or stops being one here.
        if (asset.AssetClass == AssetClass.Deposit || request.AssetClass == AssetClass.Deposit)
        {
            return DepositErrors.UseDepositEndpoints;
        }

        // The same for a treasury bond and its terms.
        if (asset.AssetClass == AssetClass.Bond || request.AssetClass == AssetClass.Bond)
        {
            return BondErrors.UseBondEndpoints;
        }

        // The same for a savings account and its terms.
        if (asset.AssetClass == AssetClass.Savings || request.AssetClass == AssetClass.Savings)
        {
            return SavingsAccountErrors.UseSavingsAccountEndpoints;
        }

        // The same for a precious metal holding.
        if (asset.AssetClass == AssetClass.PreciousMetal || request.AssetClass == AssetClass.PreciousMetal)
        {
            return MetalErrors.UseMetalEndpoints;
        }

        // Each transaction's frozen PLN rate belongs to the asset's currency, so the currency locks once there is one.
        if (!string.Equals(request.Currency, asset.Currency, StringComparison.Ordinal)
            && await dbContext.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken))
        {
            return AssetErrors.CurrencyLockedByTransactions;
        }

        // A class change must not leave transactions of a type the new class does not accept.
        var allowedTypes = AssetTransactionTypes.Allowed(request.AssetClass);
        var disallowed = await dbContext.Transactions
            .Where(t => t.AssetId == assetId && !allowedTypes.Contains(t.Type))
            .Select(t => (TransactionType?)t.Type)
            .FirstOrDefaultAsync(cancellationToken);
        if (disallowed is { } disallowedType)
        {
            return TransactionErrors.TypeNotAllowedForClass(disallowedType, request.AssetClass);
        }

        // Switching modes leaves the transactions untouched; only the valuation fields below move.
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
            asset.ManualValueAmount = null;
            asset.ManualValueDate = null;
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
            asset.InstrumentId = null;
            asset.ValuationMode = AssetValuationMode.Manual;
        }
        else
        {
            // Currency-valued: clear both other modes' fields so a switch leaves no stale data.
            asset.InstrumentId = null;
            asset.ManualValueAmount = null;
            asset.ManualValueDate = null;
            asset.ValuationMode = AssetValuationMode.CurrencyValued;
        }

        asset.AssetClass = request.AssetClass;
        asset.Name = request.Name;
        asset.Currency = request.Currency;

        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        // This slice never writes transactions, so the count is the same before and after the save.
        var transactionCount = await dbContext.Transactions.CountAsync(t => t.AssetId == assetId, cancellationToken);

        return asset.ToResponse(transactionCount);
    }
}

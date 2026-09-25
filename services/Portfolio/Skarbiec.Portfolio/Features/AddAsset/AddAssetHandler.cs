using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
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
            // Currency-valued (M1.4) is the fallthrough: neither branch below applies when the
            // request supplies neither InstrumentId nor ManualValue — AddAssetRequest.Validate already
            // confirmed that combination is only accepted for classes that support it.
            ValuationMode = AssetValuationMode.CurrencyValued,
        };

        if (request.InstrumentId is { } instrumentId)
        {
            var lookupStatus = await instrumentLookupClient.CheckAsync(instrumentId, cancellationToken);
            if (lookupStatus == InstrumentLookupStatus.NotFound)
            {
                return AssetErrors.InstrumentNotFound(instrumentId);
            }

            if (lookupStatus == InstrumentLookupStatus.Unavailable)
            {
                return AssetErrors.MarketDataUnavailable;
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

        // M1.5: the optional "add first transaction" checkbox. Fully validated (Money, then
        // TransactionQuantityCalculator.Recompute — the single ADR-009 path RecordTransaction also
        // uses) *before* anything below is added to the change tracker, so a failure here — the
        // asset's own Money/instrument checks above already follow the same rule — returns with
        // nothing staged: no half-created asset, no orphaned transaction (AC: validation failure in
        // the transaction half leaves no half-created asset behind).
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

            // A brand-new asset has no prior history, so the "recompute over full history" rule
            // (ADR-009) degenerates to recomputing over this one candidate transaction — same
            // calculator, same oversell check (a Sell/Withdraw as a first transaction fails here,
            // exactly as it would against an empty position through RecordTransaction).
            var recomputed = TransactionQuantityCalculator.Recompute([initialTransaction]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            // Same rate resolution as RecordTransaction (ADR-026), after every other check and still
            // before anything is staged — MarketData being down leaves no half-created asset either.
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

        // One event for the finished position, published before SaveChangesAsync so the outbox row
        // commits in the same transaction as the rows above (ADR-012) — the optional initial
        // transaction is already folded into asset.Quantity, so a consumer sees the asset's real
        // opening state, not a zero it would have to correct a moment later (spec-02 AC-1).
        await positionEventPublisher.PublishCreatedAsync(asset, portfolio, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToResponse(initialTransaction is null ? 0 : 1);
    }
}

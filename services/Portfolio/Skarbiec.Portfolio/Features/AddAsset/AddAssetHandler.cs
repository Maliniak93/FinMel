using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed class AddAssetHandler(PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, IInstrumentLookupClient instrumentLookupClient)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, AddAssetRequest request, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(portfolioId);
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

            var fee = Money.Create(transactionRequest.Fee, asset.Currency);
            if (fee.IsFailure)
            {
                return fee.Error;
            }

            initialTransaction = new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                Type = transactionRequest.Type,
                Quantity = transactionRequest.Quantity,
                UnitPriceAmount = unitPrice.Value.Amount,
                FeeAmount = fee.Value.Amount,
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

            asset.Quantity = recomputed.Value;
        }

        dbContext.Assets.Add(asset);
        portfolio.AssetCount++;

        // Published before SaveChangesAsync since the interceptor only stamps Asset.UserId during
        // that call (ADR-006) — dbContext.CurrentUserId is available immediately (ADR-012: publish
        // inside the same SaveChanges as the business write).
        await publishEndpoint.Publish(new AssetChanged
        {
            AssetId = asset.Id,
            PortfolioId = portfolioId,
            UserId = dbContext.CurrentUserId,
            Kind = AssetChangeKind.Created
        }, cancellationToken);

        if (initialTransaction is not null)
        {
            dbContext.Transactions.Add(initialTransaction);
            asset.TransactionCount++;

            await publishEndpoint.Publish(new TransactionRecorded
            {
                TransactionId = initialTransaction.Id,
                AssetId = asset.Id,
                UserId = dbContext.CurrentUserId,
                Type = initialTransaction.Type,
                Quantity = initialTransaction.Quantity,
                Date = initialTransaction.Date
            }, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToResponse();
    }
}

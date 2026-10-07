using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Metals.AddMetal;

public sealed class AddMetalHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    TimeProvider timeProvider)
{
    public async Task<Result<MetalResponse>> HandleAsync(Guid portfolioId, AddMetalRequest request, CancellationToken cancellationToken)
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

        if (request.FirstPurchase is { } purchase && purchase.Date > WarsawCalendar.Today(timeProvider))
        {
            return MetalErrors.PurchaseDateInFuture;
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            AssetClass = AssetClass.PreciousMetal,
            Name = request.Name,
            Currency = Money.BaseCurrency,
            ValuationMode = AssetValuationMode.Market,
            InstrumentId = MetalInstruments.InstrumentIdFor(request.Metal),
        };

        var holding = new MetalHolding
        {
            AssetId = asset.Id,
            Metal = request.Metal,
            FineWeightGramsPerPiece = MetalWeight.ToGrams(request.FineWeight, request.WeightUnit)
        };

        // The first purchase is an ordinary Buy, editable and deletable later like any other.
        Transaction? buy = null;
        if (request.FirstPurchase is { } firstPurchase)
        {
            var unitPrice = Money.Create(firstPurchase.PricePerPiece, asset.Currency);
            if (unitPrice.IsFailure)
            {
                return unitPrice.Error;
            }

            buy = new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                Type = TransactionType.Buy,
                Quantity = firstPurchase.Pieces,
                UnitPriceAmount = unitPrice.Value.Amount,
                FxRateToPln = 1m,
                Date = firstPurchase.Date
            };

            var recomputed = TransactionQuantityCalculator.Recompute([buy]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            asset.Quantity = recomputed.Value;
        }

        dbContext.Assets.Add(asset);
        dbContext.MetalHoldings.Add(holding);
        if (buy is not null)
        {
            dbContext.Transactions.Add(buy);
        }

        await positionEventPublisher.PublishCreatedAsync(asset, portfolio, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return holding.ToResponse(asset, portfolio.Name, portfolio.IsArchived);
    }
}

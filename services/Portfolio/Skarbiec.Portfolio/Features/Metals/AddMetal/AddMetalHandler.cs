using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Metals.AddMetal;

public sealed class AddMetalHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result<MetalResponse>> HandleAsync(AddMetalRequest request, CancellationToken cancellationToken)
    {
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
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

        dbContext.Assets.Add(asset);
        dbContext.MetalHoldings.Add(holding);

        await positionEventPublisher.PublishCreatedAsync(asset, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return holding.ToResponse(asset);
    }
}

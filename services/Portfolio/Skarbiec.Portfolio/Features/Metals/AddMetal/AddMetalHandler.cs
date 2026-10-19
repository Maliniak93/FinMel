using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.AssetGroups;

namespace Skarbiec.Portfolio.Features.Metals.AddMetal;

public sealed class AddMetalHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result<MetalResponse>> HandleAsync(AddMetalRequest request, CancellationToken cancellationToken)
    {
        if (await dbContext.CheckAsync(request.GroupId, cancellationToken) is { } groupError)
        {
            return groupError;
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            GroupId = request.GroupId,
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

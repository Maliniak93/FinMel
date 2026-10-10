namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed class AddAssetHandler(IPublishEndpoint publishEndpoint)
{
    public async Task HandleAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await publishEndpoint.Publish(new AssetPositionChanged { AssetId = assetId }, cancellationToken);
    }
}

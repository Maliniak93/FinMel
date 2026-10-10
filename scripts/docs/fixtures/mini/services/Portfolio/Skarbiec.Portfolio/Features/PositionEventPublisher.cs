namespace Skarbiec.Portfolio.Features;

public sealed class PositionEventPublisher(IPublishEndpoint publishEndpoint)
{
    public async Task PublishArchivedAsync(Guid portfolioId, CancellationToken cancellationToken)
        => await PublishAsync(portfolioId, cancellationToken);

    private async Task PublishAsync(Guid portfolioId, CancellationToken cancellationToken)
        => await publishEndpoint.Publish(
            new PortfolioArchived { PortfolioId = portfolioId },
            cancellationToken);
}

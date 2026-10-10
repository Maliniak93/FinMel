namespace Skarbiec.Reporting.Messaging;

internal static class HistoryRebuildRequests
{
    public static async Task RequestAsync(
        IPublishEndpoint publishEndpoint,
        Guid portfolioId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await publishEndpoint.Publish(
            new PortfolioHistoryRebuildRequested { PortfolioId = portfolioId, UserId = userId },
            cancellationToken);
    }
}

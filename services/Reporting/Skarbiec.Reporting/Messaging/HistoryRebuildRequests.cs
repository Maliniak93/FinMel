using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

internal static class HistoryRebuildRequests
{
    // One statement, so concurrent requests coalesce on the earliest date and never lose a revision bump.
    public static async Task RequestAsync(
        ReportingDbContext db,
        IPublishEndpoint publishEndpoint,
        Guid portfolioId,
        Guid userId,
        DateOnly from,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "HistoryRebuildRequests" ("PortfolioId", "UserId", "FromDate", "Revision")
            VALUES ({portfolioId}, {userId}, {from}, 1)
            ON CONFLICT ("PortfolioId") DO UPDATE
            SET "FromDate" = LEAST("HistoryRebuildRequests"."FromDate", EXCLUDED."FromDate"),
                "Revision" = "HistoryRebuildRequests"."Revision" + 1
            """,
            cancellationToken);

        await publishEndpoint.Publish(
            new PortfolioHistoryRebuildRequested { PortfolioId = portfolioId, UserId = userId },
            cancellationToken);
    }
}

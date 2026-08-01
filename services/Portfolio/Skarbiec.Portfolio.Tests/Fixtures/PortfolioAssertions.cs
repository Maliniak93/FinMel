using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>Assertions on Portfolio invariants that outlive any single slice.</summary>
internal static class PortfolioAssertions
{
    /// <summary>
    /// AC: "Quantity always equals recompute-from-scratch after any mutation" — re-derives
    /// <see cref="Asset.Quantity"/> from the full transaction history returned by the API and
    /// compares against what the API reports for the asset (ADR-009).
    /// </summary>
    public static async Task AssertQuantityMatchesRecomputeFromScratchAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        var transactions = page.Items.Select(t => new Transaction
        {
            Id = t.Id,
            AssetId = t.AssetId,
            Type = t.Type,
            Quantity = t.Quantity,
            Date = t.Date
        });

        var recomputed = TransactionQuantityCalculator.Recompute(transactions);

        Assert.True(recomputed.IsSuccess);
        Assert.Equal(recomputed.Value, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
    }
}

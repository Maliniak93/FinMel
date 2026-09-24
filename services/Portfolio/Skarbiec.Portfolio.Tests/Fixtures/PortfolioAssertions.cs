using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>Assertions on Portfolio invariants that outlive any single slice.</summary>
internal static class PortfolioAssertions
{
    /// <summary>The error code every asset/transaction write into an archived portfolio fails with (archived-portfolio-out-of-net-worth).</summary>
    public const string PortfolioArchivedErrorCode = "Conflict.PortfolioArchived";

    /// <summary>
    /// Asserts <paramref name="response"/> is the 409 ProblemDetails a write into an archived
    /// portfolio returns: status Conflict plus the <c>errorCode</c> extension stamped by the
    /// Result→ProblemDetails mapping equal to <see cref="PortfolioArchivedErrorCode"/>.
    /// </summary>
    public static async Task AssertPortfolioArchivedConflictAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.True(
            problem.Extensions.TryGetValue("errorCode", out var errorCode),
            "The 409 ProblemDetails carries no errorCode extension.");
        var code = errorCode is JsonElement element ? element.GetString() : errorCode?.ToString();
        Assert.Equal(PortfolioArchivedErrorCode, code);
    }
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

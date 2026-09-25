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
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioArchivedErrorCode, cancellationToken);

    /// <summary>cash-transaction-types: the error code a transaction type the asset's class does not accept fails with (a top-level 400, no field key).</summary>
    public const string TransactionTypeNotAllowedErrorCode = "Validation.TransactionTypeNotAllowed";

    /// <summary>
    /// Asserts <paramref name="response"/> is the 400 a Cash/Deposit asset answers to any transaction
    /// type other than Deposit/Withdraw: <see cref="TransactionTypeNotAllowedErrorCode"/>, with a
    /// detail that names the accepted types.
    /// </summary>
    public static async Task AssertTransactionTypeNotAllowedAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.AssertProblemAsync(
            HttpStatusCode.BadRequest, TransactionTypeNotAllowedErrorCode, cancellationToken);
        Assert.NotNull(problem.Detail);
        Assert.Contains(nameof(Skarbiec.Contracts.TransactionType.Deposit), problem.Detail, StringComparison.Ordinal);
        Assert.Contains(nameof(Skarbiec.Contracts.TransactionType.Withdraw), problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserts <paramref name="response"/> is a ProblemDetails with <paramref name="status"/> and the
    /// <c>errorCode</c> extension stamped by the Result→ProblemDetails mapping equal to
    /// <paramref name="errorCode"/>; returns the problem for further assertions.
    /// </summary>
    public static async Task<ProblemDetails> AssertProblemAsync(
        this HttpResponseMessage response, HttpStatusCode status, string errorCode, CancellationToken cancellationToken)
    {
        Assert.Equal(status, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.True(
            problem.Extensions.TryGetValue("errorCode", out var rawCode),
            $"The {(int)status} ProblemDetails carries no errorCode extension.");
        var code = rawCode is JsonElement element ? element.GetString() : rawCode?.ToString();
        Assert.Equal(errorCode, code);

        return problem;
    }

    /// <summary>
    /// transactions-pln-value-and-fee-removal: a transaction as it goes over the wire carries no
    /// <c>fee</c> of any spelling. Checked on the raw JSON, because a typed read would silently drop
    /// a property the response type no longer declares.
    /// </summary>
    public static void AssertCarriesNoFee(this JsonElement transaction) =>
        Assert.DoesNotContain(
            transaction.EnumerateObject(),
            p => p.Name.StartsWith("fee", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads <paramref name="response"/>'s body as a detached JSON element, for wire-shape assertions.</summary>
    public static async Task<JsonElement> ReadJsonAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
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

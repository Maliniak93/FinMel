using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal static class PortfolioAssertions
{
    public const string PortfolioArchivedErrorCode = "Conflict.PortfolioArchived";

    public static async Task AssertPortfolioArchivedConflictAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioArchivedErrorCode, cancellationToken);

    public const string AssetArchivedErrorCode = "Conflict.AssetArchived";

    public static async Task AssertAssetArchivedConflictAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.Conflict, AssetArchivedErrorCode, cancellationToken);

    public const string UseDepositEndpointsErrorCode = "Validation.UseDepositEndpoints";

    public const string DepositTransactionsManagedErrorCode = "Conflict.DepositTransactionsManaged";

    public const string DepositNotDueErrorCode = "Conflict.DepositNotDue";

    public const string DepositAlreadySettledErrorCode = "Conflict.DepositAlreadySettled";

    public const string DepositSettledErrorCode = "Conflict.DepositSettled";

    public const string DepositNotSettledErrorCode = "Conflict.DepositNotSettled";

    public const string DepositAlreadyPaidOutErrorCode = "Conflict.DepositAlreadyPaidOut";

    public const string DepositRolledOverErrorCode = "Conflict.DepositRolledOver";

    public const string SettlementAmountsRequiredErrorCode = "Validation.SettlementAmountsRequired";

    public static async Task AssertDepositUnsettledAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken, decimal principal = 10_000m)
    {
        var deposit = await client.GetDepositAsync(portfolioId, assetId, cancellationToken);
        Assert.NotEqual(DepositStatus.Settled, deposit.Status);
        Assert.Null(deposit.SettledOn);
        Assert.Null(deposit.SettledGrossInterest);
        Assert.Null(deposit.SettledTax);

        var asset = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(principal, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        Assert.False(asset.DepositSettled);
    }

    public static async Task AssertDepositTransactionsManagedAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.Conflict, DepositTransactionsManagedErrorCode, cancellationToken);

    public const string SavingsInterestNotDueErrorCode = "Conflict.SavingsInterestNotDue";

    public const string SavingsInterestPeriodMismatchErrorCode = "Conflict.SavingsInterestPeriodMismatch";

    public const string SavingsSettlementNotLatestErrorCode = "Conflict.SavingsSettlementNotLatest";

    public const string SavingsInterestManagedErrorCode = "Conflict.SavingsInterestManaged";

    public const string OversellsPositionErrorCode = "Conflict.OversellsPosition";

    public const string UseSavingsAccountEndpointsErrorCode = "Validation.UseSavingsAccountEndpoints";

    public static async Task AssertUseSavingsAccountEndpointsAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.BadRequest, UseSavingsAccountEndpointsErrorCode, cancellationToken);

    public static async Task AssertUseDepositEndpointsAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.BadRequest, UseDepositEndpointsErrorCode, cancellationToken);

    public static async Task AssertFieldValidationErrorAsync(
        this HttpResponseMessage response, string field, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.True(
            problem.Errors.Keys.Any(k => k.Equals(field, StringComparison.OrdinalIgnoreCase)),
            $"Expected a validation error keyed '{field}', got: {string.Join(", ", problem.Errors.Keys)}.");
    }

    public const string InvalidTransferCounterpartErrorCode = "Validation.InvalidTransferCounterpart";

    public const string InsufficientFundsErrorCode = "Validation.InsufficientFunds";

    public const string TransferLegManagedErrorCode = "Conflict.TransferLegManaged";

    public static async Task AssertInvalidTransferCounterpartAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTransferCounterpartErrorCode, cancellationToken);

    public static async Task AssertInsufficientFundsAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.BadRequest, InsufficientFundsErrorCode, cancellationToken);

    public static async Task AssertTransferLegManagedAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(HttpStatusCode.Conflict, TransferLegManagedErrorCode, cancellationToken);

    public static async Task AssertCashUntouchedAsync(
        this HttpClient client, Guid portfolioId, Guid cashId, CancellationToken cancellationToken, decimal balance = 5_000m)
    {
        var cash = await client.GetAssetAsync(portfolioId, cashId, cancellationToken);
        Assert.Equal(balance, cash.Quantity);
        var topUp = Assert.Single((await client.ListTransactionsAsync(portfolioId, cashId, cancellationToken)).Items);
        Assert.Equal(Skarbiec.Contracts.TransactionType.Deposit, topUp.Type);
        Assert.Equal(balance, topUp.Quantity);
        Assert.Null(topUp.Transfer);
    }

    public const string TransactionTypeNotAllowedErrorCode = "Validation.TransactionTypeNotAllowed";

    public static async Task AssertTransactionTypeNotAllowedAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.AssertProblemAsync(
            HttpStatusCode.BadRequest, TransactionTypeNotAllowedErrorCode, cancellationToken);
        Assert.NotNull(problem.Detail);
        Assert.Contains(nameof(Skarbiec.Contracts.TransactionType.Deposit), problem.Detail, StringComparison.Ordinal);
        Assert.Contains(nameof(Skarbiec.Contracts.TransactionType.Withdraw), problem.Detail, StringComparison.Ordinal);
    }

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

    public static void AssertCarriesNoFee(this JsonElement transaction) =>
        Assert.DoesNotContain(
            transaction.EnumerateObject(),
            p => p.Name.StartsWith("fee", StringComparison.OrdinalIgnoreCase));

    public static async Task<JsonElement> ReadJsonAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

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

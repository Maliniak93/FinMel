using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.RecordTransaction;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// Portfolio's HTTP surface as arrange-step helpers: route builders, plus the "get me a portfolio /
/// an asset / a transaction to act on" calls that nearly every slice test needs before it can
/// exercise the endpoint it actually cares about.
/// </summary>
/// <remarks>
/// Arrange only. A test asserting on one of these endpoints must call it directly and assert on the
/// raw <see cref="HttpResponseMessage"/> — the helpers here <c>EnsureSuccessStatusCode</c>, which
/// would turn the very failure such a test is looking for into an exception.
/// </remarks>
internal static class PortfolioApi
{
    public const string PortfoliosUri = "/api/portfolio/portfolios";

    public static string PortfolioUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}";

    public static string AssetsUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}/assets";

    public static string AssetUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}";

    public static string TransactionsUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions";

    public static string TransactionUri(Guid portfolioId, Guid assetId, Guid transactionId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions/{transactionId}";

    /// <summary>Creates a portfolio and returns its id. Pass distinct <paramref name="name"/>s — the name is unique per user.</summary>
    public static async Task<Guid> CreatePortfolioAsync(
        this HttpClient client, CancellationToken cancellationToken, string name = "Retirement")
    {
        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = name, Currency = "PLN" }, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken))!.Id;
    }

    /// <summary>
    /// Adds an asset to <paramref name="portfolioId"/> and returns its id. Quantity is left unset,
    /// so the asset starts at 0 and its quantity is driven purely by transactions (ADR-009).
    /// </summary>
    public static async Task<Guid> AddAssetAsync(
        this HttpClient client,
        Guid portfolioId,
        CancellationToken cancellationToken,
        string name = "Test asset",
        AssetClass assetClass = AssetClass.Stock,
        decimal manualValue = 0m)
    {
        var request = new AddAssetRequest
        {
            AssetClass = assetClass,
            Name = name,
            Currency = "PLN",
            ManualValue = manualValue,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!.Id;
    }

    /// <summary>The common arrange step: a portfolio holding one asset, both owned by <paramref name="client"/>'s user.</summary>
    public static async Task<(Guid PortfolioId, Guid AssetId)> CreatePortfolioWithAssetAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken);

        return (portfolioId, assetId);
    }

    /// <summary>Records a transaction and returns its id.</summary>
    public static async Task<Guid> RecordTransactionAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        TransactionType type,
        decimal quantity,
        DateOnly date,
        CancellationToken cancellationToken,
        decimal unitPrice = 10m)
    {
        var request = new RecordTransactionRequest { Type = type, Quantity = quantity, UnitPrice = unitPrice, Date = date };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!.Id;
    }

    public static async Task<AssetResponse> GetAssetAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
    }

    public static async Task<PagedResponse<TransactionResponse>> ListTransactionsAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(TransactionsUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken))!;
    }
}

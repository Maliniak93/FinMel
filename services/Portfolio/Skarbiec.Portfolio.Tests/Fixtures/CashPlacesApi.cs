using System.Net.Http.Json;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of a cash place endpoint calls it directly.
internal static class CashPlacesApi
{
    public const string CashPlacesUri = "/api/portfolio/cash-places";

    public const string CashPlaceManagedErrorCode = "Conflict.CashPlaceManaged";

    public const string CashPlaceArchivedErrorCode = "Conflict.CashPlaceArchived";

    public const string CashPlaceNotEmptyErrorCode = "Conflict.CashPlaceNotEmpty";

    public const string CashPlaceHasHistoryErrorCode = "Conflict.CashPlaceHasHistory";

    public const string DuplicateCurrencyErrorCode = "Conflict.DuplicateCurrency";

    public const string UseCashPlaceEndpointsErrorCode = "Validation.UseCashPlaceEndpoints";

    public sealed record CashBalanceView(string Currency, Guid CashAssetId, decimal Balance, decimal? ValuePln);

    public sealed record CashPlaceView(
        Guid Id, string Name, bool IsArchived, List<CashBalanceView> Balances, decimal? ValuePln)
    {
        public Guid AssetId(string currency = "PLN") => Balances.Single(b => b.Currency == currency).CashAssetId;
    }

    public sealed record CashTotalView(string Currency, decimal Balance);

    public sealed record CashPlacesView(
        List<CashPlaceView> Places, List<CashTotalView> Totals, decimal? TotalValuePln, bool FxUnavailable);

    public static string CashPlaceUri(Guid placeId) => $"{CashPlacesUri}/{placeId}";

    public static string ArchiveCashPlaceUri(Guid placeId) => $"{CashPlaceUri(placeId)}/archive";

    public static string RestoreCashPlaceUri(Guid placeId) => $"{CashPlaceUri(placeId)}/restore";

    public static string CashPlaceCurrenciesUri(Guid placeId) => $"{CashPlaceUri(placeId)}/currencies";

    public static object NewCashPlaceBody(string name = "Sejf", params string[] currencies) =>
        new { name, currencies = currencies.Length == 0 ? ["PLN"] : currencies };

    public static string CashPlaceCandidatesUri(string currency = "PLN") =>
        $"/api/portfolio/transfer-candidates?cashPlaces=true&currency={Uri.EscapeDataString(currency)}";

    public static async Task<CashPlaceView> CreateCashPlaceAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "Sejf",
        params string[] currencies)
    {
        var response = await client.PostAsJsonAsync(CashPlacesUri, NewCashPlaceBody(name, currencies), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CashPlaceView>(cancellationToken))!;
    }

    public static async Task<CashPlaceView> CreateFundedCashPlaceAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal balance,
        DateOnly depositedOn,
        string name = "Sejf",
        string currency = "PLN")
    {
        var place = await client.CreateCashPlaceAsync(cancellationToken, name, currency);
        await client.RecordTransactionAsync(
            place.AssetId(currency), TransactionType.Deposit, balance, depositedOn, cancellationToken, unitPrice: 1m);

        return place;
    }

    public static async Task<CashPlacesView> ListCashPlacesAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(CashPlacesUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CashPlacesView>(cancellationToken))!;
    }

    public static async Task<CashPlaceView> GetCashPlaceAsync(
        this HttpClient client, Guid placeId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(CashPlaceUri(placeId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CashPlaceView>(cancellationToken))!;
    }

    public static async Task ArchiveCashPlaceAsync(
        this HttpClient client, Guid placeId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(ArchiveCashPlaceUri(placeId), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<decimal> GetBalanceAsync(
        this HttpClient client, Guid cashAssetId, CancellationToken cancellationToken) =>
        (await client.GetAssetAsync(cashAssetId, cancellationToken)).Quantity;

    public static async Task AssertCashPlaceManagedAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.AssertProblemAsync(System.Net.HttpStatusCode.Conflict, CashPlaceManagedErrorCode, cancellationToken);
}

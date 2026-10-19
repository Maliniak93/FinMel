using System.Net.Http.Json;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of an asset group endpoint calls it directly.
internal static class AssetGroupsApi
{
    public const string AssetGroupsUri = "/api/portfolio/asset-groups";

    public const string AssignmentUri = AssetGroupsUri + "/assignment";

    public const string AssignableAssetsUri = AssetGroupsUri + "/assets";

    public const string GroupLimitReachedErrorCode = "Conflict.GroupLimitReached";

    public const string GroupColorTakenErrorCode = "Conflict.GroupColorTaken";

    public const string InvalidColorErrorCode = "Validation.InvalidColor";

    public const string InvalidTargetErrorCode = "Validation.InvalidTarget";

    public const string AssetGroupNotFoundErrorCode = "NotFound.AssetGroup";

    public const string AssetNotFoundErrorCode = "NotFound.Asset";

    public const string AssetIdsRequiredErrorCode = "Validation.AssetIdsRequired";

    public const int GroupLimit = 12;

    public sealed record AssetGroupView(Guid Id, string Name, string Color, decimal? TargetPln, int AssetCount);

    public sealed record AssignableAssetView(
        Guid AssetId,
        string Name,
        AssetClass AssetClass,
        string Currency,
        Guid? AccountId,
        string? AccountName,
        string? CashPlaceName,
        Guid? GroupId,
        bool IsArchived);

    public static string AssetGroupUri(Guid groupId) => $"{AssetGroupsUri}/{groupId}";

    public static string ColorKey(int index) => AssetGroupColors.Keys[index];

    public static object NewAssetGroupBody(string name = "Poduszka", string? color = null, decimal? targetPln = null) =>
        new { name, color = color ?? ColorKey(0), targetPln };

    public static object NewAssignmentBody(IEnumerable<Guid> assetIds, Guid? groupId) =>
        new { assetIds = assetIds.ToArray(), groupId };

    public static async Task<AssetGroupView> CreateAssetGroupAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "Poduszka",
        string? color = null,
        decimal? targetPln = null)
    {
        var response = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody(name, color, targetPln), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetGroupView>(cancellationToken))!;
    }

    public static async Task<List<AssetGroupView>> ListAssetGroupsAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AssetGroupsUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<AssetGroupView>>(cancellationToken))!;
    }

    public static async Task<List<AssignableAssetView>> ListAssignableAssetsAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AssignableAssetsUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<AssignableAssetView>>(cancellationToken))!;
    }

    public static async Task AssignAssetsAsync(
        this HttpClient client, Guid? groupId, CancellationToken cancellationToken, params Guid[] assetIds)
    {
        var response = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody(assetIds, groupId), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
